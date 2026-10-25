using System.IO;
using System.Runtime.InteropServices;
using MoveSense.Core;

namespace MoveSense.Desktop;

internal static class LaunchInput
{
    public static string? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        var path = args.Length == 1 && !args[0].StartsWith("--") ? args[0] :
            args.Length == 2 && args[0] == "--source" ? args[1] : throw new IOException(Ui.T("OneItem"));
        path = Paths.Full(path);
        if (!Paths.Exists(path)) throw new IOException(Ui.T("MissingSource"));
        return path;
    }
}

internal static class DesktopIntegration
{
    public static string CreateShortcut(string? folderOverride = null)
    {
        var folder = folderOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(folder)) throw new IOException("Desktop folder is unavailable.");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "MoveSense.lnk");
        var executable = Environment.ProcessPath ?? throw new IOException("Application path is unavailable.");
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(path);
            if (File.Exists(path) && !string.Equals(Path.GetFileName((string)shortcut.TargetPath), "MoveSense.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("MoveSense.lnk already points to another application. Rename that shortcut first.");
            shortcut.TargetPath = executable;
            shortcut.Arguments = "";
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable)!;
            shortcut.IconLocation = executable + ",0";
            shortcut.Description = Ui.T("ExplorerMenu");
            shortcut.Save();
            if (!File.Exists(path)) throw new IOException("Windows could not save the shortcut.");
            return path;
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
    internal static void VerifyShortcut(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(path);
            if (!Paths.Equal((string)shortcut.TargetPath, Environment.ProcessPath!) || (string)shortcut.Arguments != "")
                throw new IOException("Shortcut target or arguments are incorrect.");
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
