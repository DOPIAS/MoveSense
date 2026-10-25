using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MoveSense.Core;

public interface IReferenceDetector
{
    bool Supports(string extension);
    IEnumerable<Reference> Detect(string owner, string text);
}

public static class ReferenceParser
{
    public static Reference? Create(string owner, string raw, int offset, int length,
        ReferenceKind kind, bool repair, bool markdown = false)
    {
        try
        {
            var value = raw;
            var suffix = "";
            if (markdown)
            {
                var split = value.IndexOfAny(['#', '?']);
                if (split >= 0) { suffix = value[split..]; value = value[..split]; }
            }
            if (string.IsNullOrWhiteSpace(value) || value.Contains("$(") || value.Contains("${") ||
                value.Contains('%') && !markdown || value.Contains('*') || value.Contains('\0'))
                return null;
            // Drive-qualified paths are accepted; URLs, shell variables and URI schemes are not.
            if (Regex.IsMatch(value, @"^[a-zA-Z][a-zA-Z\d+.-]+:") &&
                !Regex.IsMatch(value, @"^[a-zA-Z]:[\\/]")) return null;
            if (value.StartsWith("//", StringComparison.Ordinal)) return null;
            var encoded = markdown && value.Contains('%');
            if (markdown) value = Uri.UnescapeDataString(value).Replace(@"\(", "(").Replace(@"\)", ")");
            var local = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var absolute = Path.IsPathRooted(local);
            if (absolute && !Path.IsPathFullyQualified(local)) return null;
            var target = Paths.Full(absolute ? local : Path.Combine(Path.GetDirectoryName(owner)!, local));
            return new(owner, target, raw, offset, length, kind, repair, absolute, suffix, encoded);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        { return null; }
    }
}

public sealed class MarkdownDetector : IReferenceDetector
{
    private static readonly Regex Links = new(
        @"!?\[[^\]\r\n]*\]\(\s*(?:<(?<path>[^>\r\n]+)>|(?<path>(?:\\.|[^\s()\\]|\((?:\\.|[^()\\])*\))+))(?:\s+[""'][^\r\n]*?[""'])?\s*\)|^\s{0,3}\[[^\]\r\n]+\]:\s*(?:<(?<path>[^>\r\n]+)>|(?<path>\S+))",
        RegexOptions.Multiline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    public bool Supports(string extension) => extension is ".md" or ".markdown";
    private static string MaskCode(string text)
    {
        var mask = text.ToCharArray();
        char fence = '\0';
        var fenceLength = 0;
        var offset = 0;
        foreach (var line in text.Split('\n'))
        {
            var match = Regex.Match(line, @"^ {0,3}(?<fence>\x60{3,}|~{3,})", RegexOptions.None, TimeSpan.FromSeconds(1));
            var inFence = fence != '\0';
            if (match.Success)
            {
                var delimiter = match.Groups["fence"].Value;
                if (!inFence) { fence = delimiter[0]; fenceLength = delimiter.Length; inFence = true; }
                else if (delimiter[0] == fence && delimiter.Length >= fenceLength &&
                    string.IsNullOrWhiteSpace(line[match.Length..])) fence = '\0';
            }
            if (inFence || line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t'))
                for (var i = offset; i < Math.Min(offset + line.Length, mask.Length); i++)
                    if (mask[i] != '\r') mask[i] = ' ';
            offset += line.Length + 1;
        }
        var masked = new string(mask);
        return Regex.Replace(masked, @"\x60+[^\r\n]*?\x60+", m => new string(' ', m.Length),
            RegexOptions.None, TimeSpan.FromSeconds(2));
    }
    public IEnumerable<Reference> Detect(string owner, string text)
    {
        foreach (Match m in Links.Matches(MaskCode(text)))
        {
            var g = m.Groups["path"];
            var r = ReferenceParser.Create(owner, g.Value, g.Index, g.Length, ReferenceKind.Markdown, true, true);
            if (r is not null) yield return r;
        }
    }
}

public sealed class JsonDetector : IReferenceDetector
{
    public bool Supports(string extension) => extension is ".json" or ".jsonc";
    public IEnumerable<Reference> Detect(string owner, string text)
    {
        // Token offsets are byte-based; convert them to UTF-16 indexes for exact text edits.
        var bytes = Encoding.UTF8.GetBytes(text);
        var results = new List<Reference>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 128 });
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.String) continue;
            var value = reader.GetString()!;
            if (!value.Contains('/') && !value.Contains('\\') && !Path.HasExtension(value)) continue;
            var index = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.TokenStartIndex));
            var length = Encoding.UTF8.GetCharCount(bytes.AsSpan((int)reader.TokenStartIndex,
                (int)(reader.BytesConsumed - reader.TokenStartIndex)));
            var r = ReferenceParser.Create(owner, value, index, length, ReferenceKind.Json, true);
            if (r is not null && (Paths.Exists(r.Target) || value.Contains('/') || value.Contains('\\')))
                results.Add(r with { CanRepair = Paths.Exists(r.Target) });
        }
        return results;
    }
}

public sealed class ConfigurationDetector : IReferenceDetector
{
    private static readonly Regex Values = new(
        @"(?:Include|Update|Project|HintPath|path|file|folder|directory|source|target)\s*[=:]\s*[""'](?<path>[^""'\r\n]+)[""']|<HintPath>(?<path>[^<]+)</HintPath>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    public bool Supports(string extension) => extension is ".csproj" or ".vbproj" or ".fsproj" or ".props" or
        ".targets" or ".xml" or ".config" or ".ini" or ".toml" or ".yaml" or ".yml";
    public IEnumerable<Reference> Detect(string owner, string text)
    {
        var project = Path.GetExtension(owner).ToLowerInvariant() is ".csproj" or ".vbproj" or ".fsproj" or ".props" or ".targets";
        foreach (Match m in Values.Matches(text))
        {
            var g = m.Groups["path"];
            var r = ReferenceParser.Create(owner, g.Value, g.Index, g.Length,
                project ? ReferenceKind.Project : ReferenceKind.Configuration, false);
            if (r is not null) yield return r;
        }
    }
}

public sealed record TextFile(string Text, Encoding Encoding, byte[] Preamble)
{
    public static TextFile Read(string path)
    {
        return FromBytes(File.ReadAllBytes(path));
    }
    public static TextFile FromBytes(byte[] bytes)
    {
        Encoding encoding;
        var skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else encoding = new UTF8Encoding(false, true);
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        if (text.Contains('\0')) throw new IOException("Binary or unsupported text encoding.");
        return new(text, encoding, bytes[..skip]);
    }
    public byte[] Encode(string text) => Preamble.Concat(Encoding.GetBytes(text)).ToArray();
}





