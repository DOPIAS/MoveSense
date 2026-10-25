using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MoveSense.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeIndex = Array.IndexOf(e.Args, "--smoke-test");
        if (smokeIndex >= 0 && smokeIndex + 1 < e.Args.Length)
        {
            var output = Path.GetFullPath(e.Args[smokeIndex + 1]);
            var root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            Directory.CreateDirectory(Path.Combine(root, "archive"));
            File.WriteAllText(Path.Combine(root, "assets", "layout.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" />");
            File.WriteAllText(Path.Combine(root, "assets", "notes.md"), "# Design notes\n\n[Project overview](../README.md)\n");
            File.WriteAllText(Path.Combine(root, "README.md"), "# Fieldnotes\n\n![Layout](assets/layout.svg)\n\n[Design notes](assets/notes.md)\n");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"assetPath\":\"assets/layout.svg\"}");
            var window = new MainWindow(dataRootOverride: Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")[..8]));
            MainWindow = window;
            window.ShowActivated = false;
            window.Show();
            window.Dispatcher.BeginInvoke(async () =>
            {
                try { await window.SmokeAsync(root, output); Shutdown(0); }
                catch (Exception error)
                { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Shutdown(1); }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }
        Ui.SetLanguage(Preferences.Load().Language);
        if (e.Args is ["--enable-context-menu"] or ["--disable-context-menu"] or ["--context-menu-status"])
        {
            try
            {
                if (e.Args[0] == "--context-menu-status")
                    Shutdown(ExplorerIntegration.Status() == ExplorerMenuStatus.Enabled ? 0 : 1);
                else
                {
                    if (e.Args[0] == "--enable-context-menu") ExplorerIntegration.Register();
                    else ExplorerIntegration.Remove();
                    Shutdown(0);
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); Shutdown(2); }
            return;
        }
        try { MainWindow = new MainWindow(LaunchInput.Parse(e.Args)); MainWindow.Show(); }
        catch (Exception error) { MessageBox.Show(error.Message, "MoveSense"); Shutdown(1); }
    }
}

