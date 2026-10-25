using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MoveSense.Core;

namespace MoveSense.Desktop;

public sealed record ReferenceRow(Impact Impact, string Status, string Kind, string Owner, string Current);
public sealed record HistoryRow(OperationRecord Record, string Date, string Status, string Source, string Destination);

public partial class MainWindow : Window
{
    private MovePlan? plan;
    private OperationRecord? completed;
    private CancellationTokenSource? scanCancellation;
    private readonly OperationStore store;
    private readonly string dataRoot;
    private Preferences preferences;
    private bool initialized;
    private bool busy;
    private bool executing;
    private readonly bool smoke;

    public MainWindow(string? source = null, string? dataRootOverride = null)
    {
        dataRoot = dataRootOverride ?? Preferences.DataRoot;
        smoke = dataRootOverride is not null;
        preferences = smoke ? new("Light") : Preferences.Load();
        Ui.SetLanguage(preferences.Language);
        Ui.SetTheme(preferences.Theme);
        InitializeComponent();
        store = new(Path.Combine(dataRoot, "operations"));
        ThemeCombo.SelectedIndex = preferences.Theme == "Dark" ? 2 : preferences.Theme == "Light" ? 1 : 0;
        LanguageCombo.SelectedIndex = preferences.Language == "ru" ? 1 : 0;
        RepairBox.Checked += (_, _) => UpdateReview();
        RepairBox.Unchecked += (_, _) => UpdateReview();
        initialized = true;
        LocalizeColumns();
        RefreshInputLabels();
        if (!string.IsNullOrWhiteSpace(source))
        {
            SetSource(source);
            // Shell entry points start at the only remaining choice: where to put it.
            if (!smoke) Loaded += (_, _) => BrowseDestination(this, new RoutedEventArgs());
        }
        RefreshExplorerState();
        Activated += (_, _) => RefreshExplorerState();
        Closing += OnClosing;
    }

    private string? PickFolder(string title)
    {
        var picker = new OpenFolderDialog { Title = title, Multiselect = false };
        return picker.ShowDialog(this) == true ? picker.FolderName : null;
    }
    private void SetSource(string path)
    {
        path = Paths.Full(path);
        if (!Paths.Exists(path)) throw new IOException(Ui.T("MissingSource"));
        SourceBox.Text = path;
        NameBox.Text = Path.GetFileName(path);
        DestinationBox.Text = "";
        ScopeBox.Text = "";
        NearbyBox.IsEnabled = NearbyParent() is not null;
        NearbyBox.IsChecked = NearbyBox.IsEnabled;
        InvalidatePlan();
        Navigate(MovePage, MoveNav);
    }
    private string? NearbyParent()
    {
        if (string.IsNullOrEmpty(SourceBox.Text)) return null;
        var parent = Path.GetDirectoryName(SourceBox.Text);
        // Never implicitly expand a check to an entire drive.
        return parent is not null && !Paths.Equal(parent, Path.GetPathRoot(parent)!) ? parent : null;
    }
    private string DestinationPath()
    {
        var name = NameBox.Text;
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.EndsWith('.') || name.EndsWith(' ')) throw new IOException(Ui.T("InvalidName"));
        return Path.Combine(DestinationBox.Text, name);
    }
    private MoveRequest CreateRequest()
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text) || string.IsNullOrWhiteSpace(DestinationBox.Text))
            throw new IOException(Ui.T("ScopeRequired"));
        var roots = new List<string>();
        if (NearbyBox.IsChecked == true && NearbyParent() is { } parent) roots.Add(parent);
        if (!string.IsNullOrWhiteSpace(ScopeBox.Text)) roots.Add(ScopeBox.Text.Trim());
        return new(SourceBox.Text, DestinationPath(), roots);
    }
    private void BrowseFile(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var picker = new OpenFileDialog { CheckFileExists = true, Title = Ui.T("Source") };
        if (picker.ShowDialog(this) == true) TrySetSource(picker.FileName);
    }
    private void BrowseSource(object sender, RoutedEventArgs e)
    { if (!busy && PickFolder(Ui.T("Source")) is { } p) TrySetSource(p); }
    private void TrySetSource(string path)
    { try { SetSource(path); } catch (Exception error) { ShowError(error); } }
    private async void BrowseDestination(object sender, RoutedEventArgs e)
    { if (!busy && PickFolder(Ui.T("Destination")) is { } p) await ChooseDestinationAsync(p); }
    private async void ChooseDesktop(object sender, RoutedEventArgs e)
    { await ChooseDestinationAsync(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); }
    private async void ChooseDocuments(object sender, RoutedEventArgs e)
    { await ChooseDestinationAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); }
    private async Task ChooseDestinationAsync(string folder)
    {
        if (busy) return;
        DestinationBox.Text = folder;
        if (!string.IsNullOrWhiteSpace(SourceBox.Text)) await RunAnalysisAsync();
    }
    private void BrowseScope(object sender, RoutedEventArgs e)
    { if (PickFolder(Ui.T("ScanScope")) is { } p) ScopeBox.Text = p; }
    private static string[] DroppedPaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] ?? [] : [];
    private void FileDragOver(object sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e.Data);
        // Link effect only selects an input; the shell must not delete or move anything on drop.
        e.Effects = !busy && paths.Length == 1 && Paths.Exists(paths[0])
            ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }
    private void FileDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (busy) return;
        try { AcceptDrop(e.Data); }
        catch (Exception error) { ShowError(error); }
    }
    private void AcceptDrop(IDataObject data)
    {
        var paths = DroppedPaths(data);
        if (paths.Length != 1) throw new IOException(Ui.T("OneItem"));
        SetSource(paths[0]);
    }
    private void InputsChanged(object sender, TextChangedEventArgs e)
    { if (initialized && !busy) InvalidatePlan(); }
    private void OptionsChanged(object sender, RoutedEventArgs e)
    { if (initialized && !busy) InvalidatePlan(); }
    private void InvalidatePlan()
    {
        plan = null;
        completed = null;
        InputPanel.Visibility = Visibility.Visible;
        CompletionPanel.Visibility = ReviewPanel.Visibility = DetailsPanel.Visibility = Visibility.Collapsed;
        ReferencesGrid.ItemsSource = null;
        PreviewPanel.Visibility = AcknowledgeBox.Visibility = Visibility.Collapsed;
        AcknowledgeBox.IsChecked = false;
        WarningsBox.Text = "";
        ActionHint.Text = Ui.T("EmptySummary");
        RefreshInputLabels();
        UpdateActions();
        StatusText.Text = Ui.T("Ready");
    }
    private void RefreshInputLabels()
    {
        SourceTitle.Text = SourceBox.Text.Length == 0 ? Ui.T("DropTitle") : Path.GetFileName(SourceBox.Text);
        ScopeDescription.Text = SourceBox.Text.Length == 0 ? Ui.T("NearbyHelp") : NearbyBox.IsChecked == true && NearbyParent() is { } parent
            ? parent + "\n" + Ui.T("NearbyHelp") : Ui.T("OnlySource");
        try { DestinationPreview.Text = DestinationBox.Text.Length > 0 && NameBox.Text.Length > 0 ? DestinationPath() : Ui.T("DestinationHelp"); }
        catch (IOException) { DestinationPreview.Text = Ui.T("InvalidName"); }
    }
    private async void AnalyzeClick(object sender, RoutedEventArgs e) => await RunAnalysisAsync();
    private async Task RunAnalysisAsync()
    {
        if (busy) return;
        try { await AnalyzeAsync(); }
        catch (OperationCanceledException) { StatusText.Text = Ui.T("Cancelled"); }
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); scanCancellation?.Dispose(); scanCancellation = null; }
    }
    private async Task AnalyzeAsync()
    {
        var request = CreateRequest();
        InvalidatePlan();
        SetBusy(true, true);
        scanCancellation = new();
        var progress = new ThrottledProgress(path => Dispatcher.BeginInvoke(() =>
        { if (busy) StatusText.Text = Ui.T("Analyzing") + " " + path; }));
        var result = await Task.Run(() => new Planner().Analyze(request, scanCancellation.Token, progress));
        Present(result);
    }
    private void Present(MovePlan result)
    {
        plan = result;
        ReferencesGrid.ItemsSource = result.Impacts.OrderByDescending(i => i.Breaks)
            .Select(i => new ReferenceRow(i, Ui.T(!i.Breaks ? "Unchanged" : i.Reference.CanRepair ? "Repairable" : "Manual"),
                i.Reference.Kind.ToString(), Path.GetFileName(i.Reference.Owner), i.Reference.Raw)).ToArray();
        EmptyState.Visibility = result.Impacts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewPanel.Visibility = DetailsPanel.Visibility = Visibility.Visible;
        WarningsBox.Text = string.Join("\n\n", result.Blockers.Select(Ui.Explain).Concat(result.Warnings.Select(w => w.Path + "\n" + w.Message)));
        if (WarningsBox.Text.Length == 0) WarningsBox.Text = Ui.T("NoNotes");
        AcknowledgeBox.IsChecked = false;
        UpdateReview();
        DetailsPanel.IsExpanded = result.Blockers.Count > 0 || result.Warnings.Count > 0 || result.AtRisk > result.Repairable;
        ResultsTabs.SelectedIndex = result.Blockers.Count > 0 || result.Warnings.Count > 0 ? 1 : 0;
        StatusText.Text = result.Blockers.Count == 0 ? Ui.T("Ready") : string.Join(" ", result.Blockers.Select(Ui.Explain));
        ReviewPanel.BringIntoView();
    }
    private void UpdateReview()
    {
        if (!initialized || plan is null) return;
        var repairs = RepairBox.IsChecked == true ? plan.Repairable : 0;
        var unresolved = plan.AtRisk - repairs;
        var review = plan.Warnings.Count > 0 || unresolved > 0;
        ReviewTitle.Text = Ui.T(!plan.CanExecute ? "ReviewBlocked" : review ? "ReviewNeeded" : "ReviewReady");
        SummaryText.Text = !plan.CanExecute ? string.Join("\n", plan.Blockers.Select(Ui.Explain)) : plan.AtRisk == 0
            ? Ui.T("NoBrokenLinks") : string.Format(Ui.T("RepairSummary"), repairs, unresolved);
        CoverageText.Text = string.Format(Ui.T("Coverage"), plan.FilesVisited);
        AcknowledgeBox.IsChecked = false;
        AcknowledgeBox.Visibility = review && plan.CanExecute ? Visibility.Visible : Visibility.Collapsed;
        RefreshReferencePreview();
        ActionHint.Text = plan.CanExecute ? plan.Source + "\n→ " + plan.Destination : Ui.T("ReviewBlocked");
        UpdateActions();
    }
    private void AcknowledgmentChanged(object sender, RoutedEventArgs e) { if (initialized) UpdateActions(); }
    private void UpdateActions()
    {
        var canMove = plan?.CanExecute == true && completed is null;
        AnalyzeButton.Visibility = !canMove && completed is null ? Visibility.Visible : Visibility.Collapsed;
        AnalyzeButton.IsEnabled = !busy && SourceBox.Text.Length > 0 && DestinationBox.Text.Length > 0;
        MoveButton.Visibility = canMove ? Visibility.Visible : Visibility.Collapsed;
        MoveButton.IsEnabled = !busy && canMove && (AcknowledgeBox.Visibility != Visibility.Visible || AcknowledgeBox.IsChecked == true);
        QuickUndoButton.IsEnabled = !busy && completed?.Status == "Completed";
    }
    private void ReferenceSelected(object sender, SelectionChangedEventArgs e) => RefreshReferencePreview();
    private void RefreshReferencePreview()
    {
        if (ReferencesGrid.SelectedItem is not ReferenceRow row) { PreviewPanel.Visibility = Visibility.Collapsed; return; }
        var i = row.Impact;
        PreviewPanel.Visibility = Visibility.Visible;
        PreviewOwner.Text = i.Reference.Owner;
        BeforeBox.Text = Ui.T("Before") + i.Reference.Raw;
        AfterBox.Text = Ui.T("After") + (i.Breaks && i.Reference.CanRepair && RepairBox.IsChecked == true ? i.Replacement : i.Reference.Raw);
        PreviewNote.Text = i.Breaks && !i.Reference.CanRepair ? Ui.T("NoRepair") : i.Explanation;
    }
    private async void MoveClick(object sender, RoutedEventArgs e)
    {
        if (plan is null || busy || !MoveButton.IsEnabled) return;
        // This button confirms the displayed plan. No second, duplicate confirmation dialog.
        var chosen = plan;
        var repair = RepairBox.IsChecked == true;
        var acknowledge = AcknowledgeBox.IsChecked == true;
        SetBusy(true); executing = true; StatusText.Text = Ui.T("Moving");
        try { await ExecutePlanAsync(chosen, repair, acknowledge); }
        catch (Exception error) { InvalidatePlan(); ShowError(error); }
        finally { executing = false; SetBusy(false); }
    }
    private async Task ExecutePlanAsync(MovePlan chosen, bool repair, bool acknowledge)
    {
        var result = await Task.Run(() => new MoveExecutor(store).Execute(chosen, repair, acknowledge));
        ShowCompleted(result);
        RefreshHistory();
    }
    private void ShowCompleted(OperationRecord result)
    {
        completed = result;
        plan = null;
        InputPanel.Visibility = ReviewPanel.Visibility = DetailsPanel.Visibility = AcknowledgeBox.Visibility = Visibility.Collapsed;
        CompletionPanel.Visibility = Visibility.Visible;
        CompletionTitle.Text = Ui.T(result.Status == "Undone" ? "Undone" : "Completed");
        CompletedPath.Text = result.Status == "Undone" ? result.Source : result.Destination;
        ActionHint.Text = Ui.T("HistoryHelp");
        StatusText.Text = CompletionTitle.Text;
        UpdateActions();
    }
    private void OpenCompletedFolder(object sender, RoutedEventArgs e)
    {
        if (completed is null || busy) return;
        var path = completed.Status == "Undone" ? completed.Source : completed.Destination;
        var folder = Path.GetDirectoryName(path)!;
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception error) { ShowError(error); }
    }
    private void NewMoveClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SourceBox.Text = DestinationBox.Text = NameBox.Text = ScopeBox.Text = "";
        InvalidatePlan();
    }
    private async void QuickUndoClick(object sender, RoutedEventArgs e)
    { if (completed is not null) await ConfirmUndoAsync(completed); }
    private void CancelScan(object sender, RoutedEventArgs e) => scanCancellation?.Cancel();
    private void SetBusy(bool value, bool scanning = false)
    {
        busy = value;
        InputPanel.IsEnabled = !value;
        RepairBox.IsEnabled = !value;
        AcknowledgeBox.IsEnabled = !value;
        MoveNav.IsEnabled = HistoryNav.IsEnabled = SettingsNav.IsEnabled = !value;
        RefreshExplorerState();
        CancelButton.Visibility = value && scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanProgress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
        if (scanning) StatusText.Text = Ui.T("Analyzing");
    }
    private void Navigate(FrameworkElement page, Button nav)
    {
        if (busy) return;
        MovePage.Visibility = HistoryPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        foreach (var button in new[] { MoveNav, HistoryNav, SettingsNav }) button.Background = Brushes.Transparent;
        nav.Background = new SolidColorBrush(Color.FromRgb(43, 66, 73));
    }
    private void ShowMove(object sender, RoutedEventArgs e) => Navigate(MovePage, MoveNav);
    private void ShowHistory(object sender, RoutedEventArgs e)
    { Navigate(HistoryPage, HistoryNav); try { RefreshHistory(); } catch (Exception error) { ShowError(error); } }
    private void ShowSettings(object sender, RoutedEventArgs e) => Navigate(SettingsPage, SettingsNav);
    private void RefreshHistoryClick(object sender, RoutedEventArgs e)
    { try { RefreshHistory(); } catch (Exception error) { ShowError(error); } }
    private void RefreshHistory()
    {
        var records = store.List();
        HistoryGrid.ItemsSource = records.Select(r => new HistoryRow(r, r.StartedAt.ToLocalTime().ToString("g"),
            r.Status, r.Source, r.Destination)).ToArray();
        try { using var index = new HistoryIndex(Path.Combine(dataRoot, "history.db")); index.Rebuild(records); }
        catch (IOException error) { StatusText.Text = "History journal loaded; index unavailable: " + error.Message; }
    }
    private async void UndoClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row) { MessageBox.Show(this, Ui.T("SelectHistory")); return; }
        await ConfirmUndoAsync(row.Record);
    }
    private async Task ConfirmUndoAsync(OperationRecord record)
    {
        if (busy) return;
        if (MessageBox.Show(this, Ui.T("UndoConfirm") + "\n\n" + record.Destination + "\n→ " + record.Source,
            "MoveSense", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true); executing = true; StatusText.Text = Ui.T("Recovering");
        try { await UndoOperationAsync(record); }
        catch (Exception error) { ShowError(error); }
        finally { executing = false; SetBusy(false); }
    }
    private async Task UndoOperationAsync(OperationRecord record)
    {
        await Task.Run(() => new MoveExecutor(store).Undo(record.Id));
        InvalidatePlan();
        ShowCompleted(store.Read(record.Id));
        RefreshHistory();
        StatusText.Text = Ui.T("Undone");
    }
    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        preferences = preferences with { Theme = ThemeCombo.SelectedIndex == 2 ? "Dark" : ThemeCombo.SelectedIndex == 1 ? "Light" : "System" };
        Ui.SetTheme(preferences.Theme); SavePreferences();
    }
    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        preferences = preferences with { Language = LanguageCombo.SelectedIndex == 1 ? "ru" : "en" };
        Ui.SetLanguage(preferences.Language);
        LocalizeColumns(); RefreshInputLabels(); RefreshExplorerState();
        if (plan is not null) Present(plan);
        else if (completed is not null) ShowCompleted(completed);
        else { StatusText.Text = Ui.T("Ready"); ActionHint.Text = Ui.T("EmptySummary"); }
        SavePreferences();
    }
    private void LocalizeColumns()
    {
        var refs = new[] { "StatusColumn", "TypeColumn", "OwnerColumn", "PathColumn" };
        var history = new[] { "DateColumn", "StatusColumn", "SourceColumn", "DestinationColumn" };
        for (var i = 0; i < 4; i++) { ReferencesGrid.Columns[i].Header = Ui.T(refs[i]); HistoryGrid.Columns[i].Header = Ui.T(history[i]); }
    }
    private void SavePreferences() { if (!smoke) { try { preferences.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError(e); } } }
    private void CreateDesktopShortcut(object sender, RoutedEventArgs e)
    { try { DesktopIntegration.CreateShortcut(); SetupStatus.Text = StatusText.Text = Ui.T("ShortcutCreated"); } catch (Exception error) { ShowError(error); } }
    private void RegisterExplorer(object sender, RoutedEventArgs e)
    { try { ExplorerIntegration.Register(); RefreshExplorerState(); StatusText.Text = Ui.T("ExplorerAdded"); } catch (Exception error) { ShowError(error); } }
    private void RemoveExplorer(object sender, RoutedEventArgs e)
    { try { ExplorerIntegration.Remove(); RefreshExplorerState(); StatusText.Text = Ui.T("ExplorerRemoved"); } catch (Exception error) { ShowError(error); } }
    private void RefreshExplorerState()
    {
        if (!initialized) return;
        var state = smoke ? ExplorerMenuStatus.Disabled : ExplorerIntegration.Status();
        var label = state switch
        {
            ExplorerMenuStatus.Enabled => "MenuEnabled",
            ExplorerMenuStatus.NeedsRepair => "MenuNeedsRepair",
            ExplorerMenuStatus.Unavailable => "MenuUnavailable",
            _ => "MenuDisabled"
        };
        MenuStatusText.Text = SetupStatus.Text = Ui.T(label);
        var action = Ui.T(state == ExplorerMenuStatus.Enabled ? "MenuEnabledShort" : state == ExplorerMenuStatus.NeedsRepair ? "RepairMenu" : "EnableMenu");
        EnableMenuButton.Content = SettingsEnableMenuButton.Content = action;
        EnableMenuButton.IsEnabled = SettingsEnableMenuButton.IsEnabled = !busy && state != ExplorerMenuStatus.Enabled;
        RemoveMenuButton.IsEnabled = !busy && state != ExplorerMenuStatus.Disabled;
    }
    private void ShowError(Exception error) { StatusText.Text = error.Message; MessageBox.Show(this, error.Message, Ui.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (executing) { e.Cancel = true; StatusText.Text = Ui.T("Moving"); }
        else scanCancellation?.Cancel();
    }

    internal async Task SmokeAsync(string fixtureRoot, string output)
    {
        Directory.CreateDirectory(output);
        LanguageCombo.SelectedIndex = 1;
        await CaptureAsync(Path.Combine(output, "main.png"));
        var source = Path.Combine(fixtureRoot, "assets");
        AcceptDrop(new DataObject(DataFormats.FileDrop, new[] { source }));
        if (SourceBox.Text != source || DestinationBox.Text.Length != 0 || MoveButton.IsEnabled)
            throw new IOException("Drop must only select a source.");
        try { AcceptDrop(new DataObject(DataFormats.FileDrop, new[] { source, fixtureRoot })); throw new InvalidOperationException("Multiple inputs were accepted."); }
        catch (IOException) { }
        if (LaunchInput.Parse([source]) != source || LaunchInput.Parse(["--source", source]) != source)
            throw new IOException("Shell argument parsing failed.");
        try { LaunchInput.Parse([source, fixtureRoot]); throw new InvalidOperationException("Multiple shell arguments were accepted."); }
        catch (IOException) { }
        var shortcut = DesktopIntegration.CreateShortcut(Path.Combine(output, "shortcuts"));
        if (!File.Exists(shortcut)) throw new IOException("Desktop shortcut was not saved.");
        DesktopIntegration.VerifyShortcut(shortcut);
        DestinationBox.Text = Path.Combine(fixtureRoot, "archive");
        await AnalyzeAsync(); SetBusy(false);
        if (plan is null || plan.Repairable != 4 || !MoveButton.IsEnabled || DetailsPanel.IsExpanded)
            throw new IOException("Simple review did not find the expected four repairs.");
        if (plan.Destination != Path.Combine(fixtureRoot, "archive", "assets")) throw new IOException("Destination folder was not composed correctly.");
        await CaptureAsync(Path.Combine(output, "analysis.png"));
        DetailsPanel.IsExpanded = true;
        ReferencesGrid.SelectedIndex = 0;
        await CaptureAsync(Path.Combine(output, "impact.png"));
        var checkedPlan = plan;
        NearbyBox.IsChecked = false;
        await AnalyzeAsync(); SetBusy(false);
        if (plan!.ScanRoots.Any(p => Paths.Equal(p, fixtureRoot)) || plan.Repairable != 1)
            throw new IOException("Opting out of the containing folder did not narrow the check.");
        NearbyBox.IsChecked = true;
        DestinationBox.Text = fixtureRoot;
        await AnalyzeAsync(); SetBusy(false);
        if (plan!.CanExecute || MoveButton.IsEnabled || !DetailsPanel.IsExpanded)
            throw new IOException("Same-location move was not blocked in the UI.");
        DestinationBox.Text = Path.Combine(fixtureRoot, "archive");
        await AnalyzeAsync(); SetBusy(false);
        RepairBox.IsChecked = false;
        if (MoveButton.IsEnabled || AcknowledgeBox.Visibility != Visibility.Visible) throw new IOException("Unrepaired references need acknowledgment.");
        RepairBox.IsChecked = true;
        NameBox.Text = "renamed-assets";
        if (plan is not null || MoveButton.IsEnabled) throw new IOException("Rename must invalidate analysis.");
        NameBox.Text = "assets";
        await AnalyzeAsync(); SetBusy(false);
        await ExecutePlanAsync(plan!, true, false);
        if (CompletionPanel.Visibility != Visibility.Visible || !QuickUndoButton.IsEnabled) throw new IOException("Completion actions missing.");
        var result = completed!;
        await CaptureAsync(Path.Combine(output, "completed.png"));
        Navigate(HistoryPage, HistoryNav);
        await CaptureAsync(Path.Combine(output, "history.png"));
        await UndoOperationAsync(result);
        if (!Directory.Exists(source) || Directory.Exists(result.Destination) || QuickUndoButton.IsEnabled)
            throw new IOException("Quick recovery failed.");
        Navigate(SettingsPage, SettingsNav);
        await CaptureAsync(Path.Combine(output, "settings.png"));
        NewMoveClick(this, new RoutedEventArgs());
        Navigate(MovePage, MoveNav);
        Ui.SetTheme("Dark");
        await CaptureAsync(Path.Combine(output, "dark.png"));
        Width = 850; Height = 650;
        await CaptureAsync(Path.Combine(output, "compact.png"));
        LanguageCombo.SelectedIndex = 0;
        await CaptureAsync(Path.Combine(output, "english.png"));
        File.WriteAllText(Path.Combine(output, "result.json"),
            System.Text.Json.JsonSerializer.Serialize(new { success = true, references = checkedPlan.Impacts.Count,
                repairs = checkedPlan.Repairable, move = result.Status, undo = store.Read(result.Id).Status,
                sqlite = File.Exists(Path.Combine(dataRoot, "history.db")), drop = true, shellArguments = true,
                shortcut = true, quickUndo = true, renameInvalidation = true, unresolvedGate = true }));
    }
    private async Task CaptureAsync(string path)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
        var visual = (FrameworkElement)Content;
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class ThrottledProgress(Action<string> action) : IProgress<string>
    {
        private long last;
        public void Report(string value)
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref last) < 100) return;
            Interlocked.Exchange(ref last, now); action(value);
        }
    }
}
