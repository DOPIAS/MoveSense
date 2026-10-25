using System.Runtime.InteropServices;

namespace MoveSense.Core;

public sealed class Scanner
{
    public const long MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxFiles = 50000;
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", "node_modules", "bin", "obj", ".vs", ".local" };
    private readonly IReferenceDetector[] detectors;
    public Scanner(params IReferenceDetector[] extra) =>
        detectors = [new MarkdownDetector(), new JsonDetector(), new ConfigurationDetector(), .. extra];

    public ScanResult Scan(IEnumerable<string> roots, CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var refs = new List<Reference>();
        var warnings = new List<ScanWarning>();
        var stamps = new List<FileStamp>();
        var seen = new HashSet<string>(Paths.Comparer);
        var count = 0;
        foreach (var root in roots.Select(Paths.Full).Distinct(Paths.Comparer))
        {
            Paths.RejectLinks(root);
            if (!Paths.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var file in Enumerate(root, warnings, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;
                if (++count > MaxFiles) throw new IOException("Scan limit reached (50,000 files). Select smaller folders.");
                progress?.Report(file);
                try
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    var matching = detectors.Where(d => d.Supports(extension)).ToArray();
                    if (extension == ".lnk")
                    {
                        refs.AddRange(ReadShortcut(file));
                        stamps.Add(new(file, Paths.Hash(file)));
                        continue;
                    }
                    if (matching.Length == 0) continue;
                    if (new FileInfo(file).Length > MaxFileBytes)
                    { warnings.Add(new(file, "Larger than 4 MiB; reference analysis skipped.")); continue; }
                    var before = Paths.Hash(file);
                    var document = TextFile.Read(file);
                    foreach (var detector in matching) refs.AddRange(detector.Detect(file, document.Text));
                    if (before != Paths.Hash(file)) throw new IOException("File changed during analysis.");
                    stamps.Add(new(file, before));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                    System.Text.Json.JsonException or System.Text.DecoderFallbackException or
                    System.Text.RegularExpressions.RegexMatchTimeoutException or COMException)
                { warnings.Add(new(file, e.Message)); }
            }
        }
        return new(refs, warnings, stamps, count, seen.ToArray());
    }

    internal static IEnumerable<string> Enumerate(string root, List<ScanWarning> warnings, CancellationToken token)
    {
        if (File.Exists(root)) { yield return root; yield break; }
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { warnings.Add(new(dir, e.Message)); continue; }
            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { warnings.Add(new(entry, e.Message)); continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { warnings.Add(new(entry, "Symbolic link or junction skipped.")); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                { if (!Excluded.Contains(Path.GetFileName(entry))) stack.Push(entry); }
                else yield return Paths.Full(entry);
            }
        }
    }

    private static IReadOnlyList<Reference> ReadShortcut(string path)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<Reference>();
        object? shell = null, shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) throw new IOException("Windows Script Host is unavailable.");
            shell = Activator.CreateInstance(type)!;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            string target = ((dynamic)shortcut).TargetPath;
            if (!string.IsNullOrWhiteSpace(target))
                result.Add(new(path, Paths.Full(target), target, 0, 0, ReferenceKind.Shortcut, false, true));
            string working = ((dynamic)shortcut).WorkingDirectory;
            if (!string.IsNullOrWhiteSpace(working) && Path.IsPathRooted(working))
                result.Add(new(path, Paths.Full(working), working, 0, 0, ReferenceKind.Shortcut, false, true));
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
        return result;
    }
}


