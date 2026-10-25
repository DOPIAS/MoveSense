using System.Text.Json;

namespace MoveSense.Core;

public sealed class Planner(Scanner? scanner = null)
{
    private readonly Scanner scanner = scanner ?? new();
    public MovePlan Analyze(MoveRequest request, CancellationToken token = default, IProgress<string>? progress = null)
    {
        var source = Paths.Full(request.Source);
        var destination = Paths.Full(request.Destination);
        var blockers = Validate(source, destination);
        var isDir = Directory.Exists(source);
        var sourceFiles = new List<FileStamp>();
        var sourceDirs = new List<string>();
        if (Paths.Exists(source))
        {
            try
            {
                Paths.RejectLinks(source);
                Snapshot(source, sourceFiles, sourceDirs, token);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { blockers.Add(e.Message); }
        }
        var roots = request.ScanRoots.Append(source).Where(Paths.Exists).ToArray();
        if (request.ScanRoots.Any(r => !Paths.Exists(r))) blockers.Add("A selected scan folder no longer exists.");
        var scan = scanner.Scan(roots, token, progress);
        var impacts = new List<Impact>();
        foreach (var found in scan.References)
        {
            var r = found with { CanRepair = found.CanRepair && Paths.Exists(found.Target) };
            if (!Paths.Within(r.Target, source) && !Paths.Within(r.Owner, source)) continue;
            var ownerAfter = Paths.Project(r.Owner, source, destination);
            var targetAfter = Paths.Project(r.Target, source, destination);
            var value = r.Kind == ReferenceKind.Markdown ? r.Raw[..(r.Raw.Length - r.Suffix.Length)] : r.Raw;
            if (r.Kind == ReferenceKind.Markdown) value = Uri.UnescapeDataString(value);
            var local = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string actual;
            try { actual = Paths.Full(r.Absolute ? local : Path.Combine(Path.GetDirectoryName(ownerAfter)!, local)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { continue; }
            var breaks = !Paths.Equal(actual, targetAfter);
            var newValue = r.Absolute ? targetAfter : Path.GetRelativePath(Path.GetDirectoryName(ownerAfter)!, targetAfter);
            if (r.Kind == ReferenceKind.Markdown)
            {
                newValue = newValue.Replace('\\', '/');
                // Encode delimiters so repaired Markdown is always a valid destination token.
                newValue = string.Join("/", newValue.Split('/').Select((s, index) => index == 0 && s.Length == 2 && s[1] == ':' ? s : Uri.EscapeDataString(s)));
                newValue += r.Suffix;
            }
            else if (r.Raw.Contains('/') && !r.Raw.Contains('\\')) newValue = newValue.Replace('\\', '/');
            var replacement = r.Kind == ReferenceKind.Json ? JsonSerializer.Serialize(newValue) : newValue;
            var explanation = !breaks ? "Reference still resolves after the move." :
                !Paths.Exists(r.Target) ? "Target is already missing; review this reference." :
                r.CanRepair ? "Exact path replacement available." : "Review and update this reference manually.";
            impacts.Add(new(r, ownerAfter, targetAfter, replacement, breaks, explanation));
        }
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, source, destination, isDir, impacts,
            scan.Warnings, blockers.Distinct().ToArray(), sourceFiles, sourceDirs, scan.ScannedFiles, scan.FilesVisited, roots, scan.Inventory);
    }

    public static List<string> Validate(string source, string destination)
    {
        var errors = new List<string>();
        if (!Paths.Exists(source)) errors.Add("Source does not exist.");
        if (Paths.Equal(source, destination)) errors.Add("Source and destination are the same.");
        if (Paths.Exists(destination)) errors.Add("Destination already exists. Overwriting is never allowed.");
        if (Directory.Exists(source) && Paths.Within(destination, source)) errors.Add("Destination is inside the source.");
        if (Paths.Equal(source, Path.GetPathRoot(source)!)) errors.Add("Moving a drive root is not supported.");
        if (!Directory.Exists(Path.GetDirectoryName(destination))) errors.Add("Destination parent must already exist.");
        if (!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), Paths.Comparison))
            errors.Add("Cross-volume moves are not supported in this version.");
        if (source.StartsWith(@"\\") || destination.StartsWith(@"\\"))
            errors.Add("Network shares are not supported for move operations.");
        try { Paths.RejectLinks(source); Paths.RejectLinks(destination); }
        catch (IOException e) { errors.Add(e.Message); }
        return errors;
    }

    internal static void Snapshot(string source, List<FileStamp> files, List<string> dirs, CancellationToken token)
    {
        Paths.RejectLinks(source);
        if (files.Count + dirs.Count > Scanner.MaxFiles) throw new IOException("Move snapshot exceeds 50,000 entries. Select a smaller source.");
        if (File.Exists(source)) { files.Add(new(source, Paths.Hash(source))); return; }
        dirs.Add(source);
        foreach (var item in Directory.GetFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested();
            Paths.RejectLinks(item);
            if (Directory.Exists(item)) Snapshot(item, files, dirs, token);
            else files.Add(new(item, Paths.Hash(item)));
        }
    }
}



