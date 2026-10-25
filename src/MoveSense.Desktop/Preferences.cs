using System.IO;
using System.Text.Json;



namespace MoveSense.Desktop;

public sealed record Preferences(string Theme = "System", string Language = "en")
{
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoveSense");
    public static Preferences Load()
    {
        var defaults = new Preferences(Language: System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DataRoot, "settings.json"))) ?? defaults; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return defaults; }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataRoot);
        File.WriteAllText(Path.Combine(DataRoot, "settings.json"), JsonSerializer.Serialize(this));
    }
}
