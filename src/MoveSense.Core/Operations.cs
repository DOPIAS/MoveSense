using System.Text.Json;

namespace MoveSense.Core;

public sealed record RepairBackup(string OriginalPath, string CurrentPath, string BackupFile,
    string BeforeHash, string AfterHash);
public sealed record OperationRecord(Guid Id, DateTimeOffset StartedAt, string Source, string Destination,
    bool IsDirectory, string Status, List<FileStamp> SourceFiles, List<string> SourceDirectories,
    List<RepairBackup> Repairs, string? Error = null);

public sealed class OperationStore
{
    public string Root { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public OperationStore(string root)
    {
        Root = Paths.Full(root);
        Paths.RejectLinks(Root);
        Directory.CreateDirectory(Root);
    }
    public IReadOnlyList<OperationRecord> List() => Directory.GetDirectories(Root)
        .Select(p => Path.Combine(p, "operation.json")).Where(File.Exists).Select(Read)
        .OrderByDescending(r => r.StartedAt).ToArray();
    public OperationRecord Read(Guid id) => Read(Path.Combine(Folder(id), "operation.json"));
    private static OperationRecord Read(string path) => JsonSerializer.Deserialize<OperationRecord>(
        File.ReadAllText(path)) ?? throw new IOException("Invalid operation journal: " + path);
    public string Folder(Guid id) => Path.Combine(Root, id.ToString("N"));
    public void Save(OperationRecord record)
    {
        var folder = Folder(record.Id);
        Directory.CreateDirectory(folder);
        AtomicWrite(Path.Combine(folder, "operation.json"), JsonSerializer.SerializeToUtf8Bytes(record, Json));
    }
    public FileStream Lock() => new(Path.Combine(Root, "operations.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);
    internal static void AtomicWrite(string path, byte[] bytes)
    {
        Paths.RejectLinks(path);
        var temporary = path + ".movesense-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path, false);
                    break;
                }
                catch (IOException error) when (attempt < 6 &&
                    (error.HResult & 0xFFFF) is 32 or 33 or 1175 or 1176 or 1177)
                { Thread.Sleep(40 * (attempt + 1)); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class MoveExecutor(OperationStore store)
{
    public OperationRecord Execute(MovePlan plan, bool repairReferences, bool acknowledgeUnresolved,
        CancellationToken token = default)
    {
        using var operationLock = store.Lock();
        token.ThrowIfCancellationRequested();
        if (!plan.CanExecute) throw new IOException(string.Join(Environment.NewLine, plan.Blockers));
        var errors = Planner.Validate(plan.Source, plan.Destination);
        if (errors.Count > 0) throw new IOException(string.Join(Environment.NewLine, errors));
        if (Paths.Within(store.Root, plan.Source) || Paths.Within(store.Root, plan.Destination))
            throw new IOException("The history folder cannot be part of the move.");
        if ((plan.Warnings.Count > 0 || plan.Impacts.Any(i => i.Breaks && (!repairReferences || !i.Reference.CanRepair)))
            && !acknowledgeUnresolved)
            throw new InvalidOperationException("Unresolved references or incomplete analysis must be acknowledged.");
        ValidateSnapshot(plan.Source, plan.SourceFiles, plan.SourceDirectories, plan.Source, []);
        foreach (var stamp in plan.ScannedFiles)
        {
            token.ThrowIfCancellationRequested();
            Paths.RejectLinks(stamp.Path);
            if (!File.Exists(stamp.Path) || Paths.Hash(stamp.Path) != stamp.Hash)
                throw new IOException("A scanned file changed. Analyze again: " + stamp.Path);
        }

        ValidateInventory(plan);
        var record = new OperationRecord(plan.Id, DateTimeOffset.UtcNow, plan.Source, plan.Destination,
            plan.IsDirectory, "Preparing", plan.SourceFiles.ToList(), plan.SourceDirectories.ToList(), []);
        if (Directory.Exists(store.Folder(record.Id))) throw new IOException("This plan was already used. Analyze again.");
        store.Save(record);
        var edits = new Dictionary<string, byte[]>(Paths.Comparer);
        try
        {
            if (repairReferences)
            {
                foreach (var group in plan.Impacts.Where(i => i.Breaks && i.Reference.CanRepair).GroupBy(i => i.Reference.Owner, Paths.Comparer))
                {
                    token.ThrowIfCancellationRequested();
                    var originalBytes = File.ReadAllBytes(group.Key);
                    var document = TextFile.FromBytes(originalBytes);
                    var stamp = plan.ScannedFiles.Single(s => Paths.Equal(s.Path, group.Key));
                    if (Paths.Hash(originalBytes) != stamp.Hash)
                        throw new IOException("A reference changed while preparing backups: " + group.Key);
                    var text = document.Text;
                    var previousOffset = text.Length;
                    foreach (var impact in group.OrderByDescending(i => i.Reference.Offset))
                    {
                        var r = impact.Reference;
                        if (r.Offset + r.Length > previousOffset) throw new IOException("Overlapping reference edits.");
                        text = text.Remove(r.Offset, r.Length).Insert(r.Offset, impact.Replacement);
                        previousOffset = r.Offset;
                    }
                    var updated = document.Encode(text);
                    var backup = record.Repairs.Count + ".original";
                    OperationStore.AtomicWrite(Path.Combine(store.Folder(record.Id), backup), originalBytes);
                    var current = Paths.Project(group.Key, plan.Source, plan.Destination);
                    record.Repairs.Add(new(group.Key, current, backup, Paths.Hash(originalBytes), Paths.Hash(updated)));
                    edits[current] = updated;
                }
            }
            // Revalidate after all potentially slow backup work, before the first user-file mutation.
            token.ThrowIfCancellationRequested();
            ValidateSnapshot(plan.Source, plan.SourceFiles, plan.SourceDirectories, plan.Source, []);
            foreach (var stamp in plan.ScannedFiles)
                if (!File.Exists(stamp.Path) || Paths.Hash(stamp.Path) != stamp.Hash)
                    throw new IOException("A scanned file changed. Analyze again: " + stamp.Path);
            ValidateInventory(plan);
            record = record with { Status = "Prepared" };
            store.Save(record);
            Move(plan.Source, plan.Destination, plan.IsDirectory);
            record = record with { Status = "Moved" };
            store.Save(record);
            // Cancellation is deferred after the move starts, so a cancellation cannot strand half a repair.
            foreach (var repair in record.Repairs)
            {
                Paths.RejectLinks(repair.CurrentPath);
                if (Paths.Hash(repair.CurrentPath) != repair.BeforeHash)
                    throw new IOException("A reference changed after moving; recovery is required: " + repair.CurrentPath);
                OperationStore.AtomicWrite(repair.CurrentPath, edits[repair.CurrentPath]);
            }
            record = record with { Status = "Completed" };
            store.Save(record);
            return record;
        }
        catch (Exception e)
        {
            record = record with { Status = "Needs recovery", Error = e.Message };
            store.Save(record);
            throw new IOException("Operation stopped. Use History to recover. " + e.Message, e);
        }
    }

    public OperationRecord Undo(Guid id)
    {
        using var operationLock = store.Lock();
        var record = store.Read(id);
        if (record.Status == "Undone") throw new IOException("This operation has already been undone.");
        var sourceExists = Paths.Exists(record.Source);
        var destinationExists = Paths.Exists(record.Destination);
        if (sourceExists == destinationExists)
            throw new IOException("Recovery requires exactly one of source or destination to exist. No files were changed.");
        var moved = destinationExists;
        var currentRoot = moved ? record.Destination : record.Source;
        Paths.RejectLinks(currentRoot);
        Paths.RejectLinks(record.Source);
        ValidateSnapshot(currentRoot, record.SourceFiles, record.SourceDirectories, record.Source, record.Repairs);
        foreach (var repair in record.Repairs)
        {
            var path = moved ? repair.CurrentPath : repair.OriginalPath;
            Paths.RejectLinks(path);
            if (!File.Exists(path)) throw new IOException("A reference file is missing: " + path);
            var hash = Paths.Hash(path);
            if (hash != repair.AfterHash && hash != repair.BeforeHash)
                throw new IOException("A reference has newer edits. Recovery refused: " + path);
            var backup = Path.Combine(store.Folder(id), repair.BackupFile);
            if (!File.Exists(backup) || Paths.Hash(backup) != repair.BeforeHash)
                throw new IOException("A backup is missing or damaged. Recovery refused.");
        }
        // All checks precede mutations. A partially interrupted recovery can be resumed.
        record = record with { Status = "Recovering" };
        store.Save(record);
        foreach (var repair in record.Repairs)
        {
            var path = moved ? repair.CurrentPath : repair.OriginalPath;
            if (Paths.Hash(path) == repair.AfterHash)
                OperationStore.AtomicWrite(path, File.ReadAllBytes(Path.Combine(store.Folder(id), repair.BackupFile)));
        }
        ValidateSnapshot(currentRoot, record.SourceFiles, record.SourceDirectories, record.Source, record.Repairs);
        if (moved) Move(record.Destination, record.Source, record.IsDirectory);
        record = record with { Status = "Undone", Error = null };
        store.Save(record);
        return record;
    }

    private void ValidateInventory(MovePlan plan)
    {
        var warnings = new List<ScanWarning>();
        var current = plan.ScanRoots.SelectMany(root => Scanner.Enumerate(root, warnings, CancellationToken.None))
            .Where(p => !Paths.Within(p, store.Root)).ToHashSet(Paths.Comparer);
        var original = plan.ScanInventory.Where(p => !Paths.Within(p, store.Root)).ToHashSet(Paths.Comparer);
        if (!current.SetEquals(original))
            throw new IOException("Files were added or removed in the scan scope. Analyze again.");
    }
    private static void Move(string source, string destination, bool isDirectory)
    {
        Paths.RejectLinks(source);
        Paths.RejectLinks(destination);
        if (Paths.Exists(destination)) throw new IOException("Destination exists; overwriting is refused.");
        if (isDirectory) Directory.Move(source, destination);
        else File.Move(source, destination, false);
    }

    private static void ValidateSnapshot(string currentRoot, IReadOnlyList<FileStamp> expected,
        IReadOnlyList<string> expectedDirs, string originalRoot, IReadOnlyList<RepairBackup> repairs)
    {
        var actual = new List<FileStamp>();
        var dirs = new List<string>();
        Planner.Snapshot(currentRoot, actual, dirs, CancellationToken.None);
        var expectedNames = expected.Select(s => Paths.Project(s.Path, originalRoot, currentRoot)).ToHashSet(Paths.Comparer);
        if (actual.Count != expected.Count || actual.Any(a => !expectedNames.Contains(a.Path)) ||
            !dirs.ToHashSet(Paths.Comparer).SetEquals(expectedDirs.Select(d => Paths.Project(d, originalRoot, currentRoot))))
            throw new IOException("Source contents changed since analysis. Analyze again; recovery never removes newer files.");
        var actualByPath = actual.ToDictionary(a => a.Path, a => a.Hash, Paths.Comparer);
        foreach (var stamp in expected)
        {
            var path = Paths.Project(stamp.Path, originalRoot, currentRoot);
            var actualHash = actualByPath[path];
            var repair = repairs.FirstOrDefault(r => Paths.Equal(r.OriginalPath, stamp.Path));
            if (actualHash != stamp.Hash && actualHash != repair?.AfterHash)
                throw new IOException("File changed since analysis or move: " + path);
        }
    }
}



