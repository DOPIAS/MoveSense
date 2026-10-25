using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace MoveSense.Desktop;

internal enum ExplorerMenuStatus { Disabled, Enabled, NeedsRepair, Unavailable }

internal static class ExplorerIntegration
{
    private static readonly string[] Keys = [@"Software\Classes\*\shell\MoveSense", @"Software\Classes\Directory\shell\MoveSense"];
    private static string Executable => Environment.ProcessPath ?? throw new IOException("Cannot determine the application path.");
    internal static string CommandFor(string executable) => "\"" + executable + "\" --source \"%1\"";

    public static ExplorerMenuStatus Status()
    {
        try
        {
            var present = 0;
            var matching = 0;
            foreach (var name in Keys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(name);
                if (key is null) continue;
                present++;
                using var command = key.OpenSubKey("command");
                if (string.Equals(command?.GetValue("") as string, CommandFor(Executable), StringComparison.OrdinalIgnoreCase)
                    && key.GetValue("MultiSelectModel") as string == "Single"
                    && !string.IsNullOrWhiteSpace(key.GetValue("") as string)) matching++;
            }
            return matching == Keys.Length ? ExplorerMenuStatus.Enabled : present == 0 ? ExplorerMenuStatus.Disabled : ExplorerMenuStatus.NeedsRepair;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        { return ExplorerMenuStatus.Unavailable; }
    }
    public static void Register()
    {
        var executable = Executable;
        foreach (var name in Keys)
        {
            using var key = Registry.CurrentUser.CreateSubKey(name);
            key.SetValue("", Ui.T("ExplorerMenu"));
            key.SetValue("MultiSelectModel", "Single");
            key.SetValue("Icon", "\"" + executable + "\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", CommandFor(executable));
        }
        NotifyShell();
        if (Status() != ExplorerMenuStatus.Enabled) throw new IOException(Ui.T("ExplorerFailed"));
    }
    public static void Remove()
    {
        foreach (var key in Keys) Registry.CurrentUser.DeleteSubKeyTree(key, false);
        NotifyShell();
        if (Status() != ExplorerMenuStatus.Disabled) throw new IOException(Ui.T("ExplorerFailed"));
    }
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
