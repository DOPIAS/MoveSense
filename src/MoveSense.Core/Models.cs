using System.Security.Cryptography;

namespace MoveSense.Core;

public enum ReferenceKind { Markdown, Json, Project, Configuration, Shortcut }
public sealed record Reference(string Owner, string Target, string Raw, int Offset, int Length,
    ReferenceKind Kind, bool CanRepair, bool Absolute, string Suffix = "", bool UrlEncoded = false);
public sealed record Impact(Reference Reference, string OwnerAfter, string TargetAfter,
    string Replacement, bool Breaks, string Explanation);
public sealed record ScanWarning(string Path, string Message);
public sealed record FileStamp(string Path, string Hash);
public sealed record ScanResult(IReadOnlyList<Reference> References, IReadOnlyList<ScanWarning> Warnings,
    IReadOnlyList<FileStamp> ScannedFiles, int FilesVisited, IReadOnlyList<string> Inventory);
public sealed record MoveRequest(string Source, string Destination, IReadOnlyList<string> ScanRoots);
public sealed record MovePlan(Guid Id, DateTimeOffset CreatedAt, string Source, string Destination,
    bool IsDirectory, IReadOnlyList<Impact> Impacts, IReadOnlyList<ScanWarning> Warnings,
    IReadOnlyList<string> Blockers, IReadOnlyList<FileStamp> SourceFiles,
    IReadOnlyList<string> SourceDirectories, IReadOnlyList<FileStamp> ScannedFiles, int FilesVisited,
    IReadOnlyList<string> ScanRoots, IReadOnlyList<string> ScanInventory)
{
    public int AtRisk => Impacts.Count(i => i.Breaks);
    public int Repairable => Impacts.Count(i => i.Breaks && i.Reference.CanRepair);
    public bool CanExecute => Blockers.Count == 0;
}

public static class Paths
{
    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Equal(string a, string b) => string.Equals(Full(a), Full(b), Comparison);
    public static bool Within(string path, string root) => Equal(path, root) ||
        Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, Comparison);
    public static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
    public static string Project(string path, string source, string destination) =>
        Within(path, source) ? (Equal(path, source) ? destination :
            Path.Combine(destination, Path.GetRelativePath(source, path))) : path;
    public static void RejectLinks(string path)
    {
        for (var p = Full(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
        {
            if (Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are not supported: " + p);
        }
    }
    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}


