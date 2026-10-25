using System.Text;
using MoveSense.Core;
using Xunit;

namespace MoveSense.Core.Tests;

public sealed class MoveTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.GetEnvironmentVariable("MOVESENSE_TEST_ROOT") ?? Path.GetTempPath(), "MoveSense-tests-" + Guid.NewGuid().ToString("N"));
    private string P(string relative) => Path.Combine(root, relative);
    private string Write(string relative, string text)
    {
        var path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }
    private MovePlan Plan(string source = "assets", string destination = "archive") =>
        new Planner().Analyze(new(P(source), P(destination), [root]));
    private MoveExecutor Executor => new(new OperationStore(P("history")));
    public MoveTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact] public void MarkdownMoveRepairsInboundAndOutboundAndUndoRestoresBytes()
    {
        Write("assets/photo.png", "image");
        Write("assets/notes.md", "[readme](../README.md#start)");
        Write("README.md", "![photo](assets/photo.png)\r\n");
        var original = File.ReadAllBytes(P("README.md"));
        var plan = Plan(destination: "moved");
        Assert.Equal(1, plan.AtRisk); // Both roots are siblings; outbound remains valid.
        var operation = Executor.Execute(plan, true, false);
        Assert.Contains("moved/photo.png", File.ReadAllText(P("README.md")));
        Assert.True(File.Exists(P("moved/photo.png")));
        Executor.Undo(operation.Id);
        Assert.True(File.Exists(P("assets/photo.png")));
        Assert.Equal(original, File.ReadAllBytes(P("README.md")));
    }

    [Fact] public void NestedDestinationRepairsOutgoingLinks()
    {
        Write("assets/notes.md", "[home](../README.md)");
        Write("README.md", "home");
        Directory.CreateDirectory(P("nested"));
        var plan = Plan(destination: "nested/assets");
        Assert.Equal(1, plan.AtRisk);
        Executor.Execute(plan, true, false);
        Assert.Contains("../../README.md", File.ReadAllText(P("nested/assets/notes.md")));
    }

    [Fact] public void InternalRelativeLinksRemainUnchanged()
    {
        Write("assets/a.md", "[photo](photo.png)");
        Write("assets/photo.png", "photo");
        var plan = Plan();
        Assert.Equal(0, plan.AtRisk);
        Assert.Single(plan.Impacts);
    }

    [Fact] public void UnicodeJsonTokenOffsetsAreExact()
    {
        Write("assets/фото.png", "photo");
        Write("settings.json", "{\"имя\":\"Дима\", \"path\":\"assets/фото.png\"}");
        var plan = Plan();
        Assert.Equal(1, plan.Repairable);
        Executor.Execute(plan, true, false);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(P("settings.json")));
        Assert.Equal("archive/фото.png", document.RootElement.GetProperty("path").GetString());
    }

    [Fact] public void EncodedMarkdownPreservesFragment()
    {
        Write("assets/a b.png", "photo");
        Write("README.md", "[photo](assets/a%20b.png#preview)");
        Executor.Execute(Plan(), true, false);
        Assert.Equal("[photo](archive/a%20b.png#preview)", File.ReadAllText(P("README.md")));
    }

    [Fact] public void Utf16AndBomSurviveRepairAndUndo()
    {
        Write("assets/a.png", "image");
        File.WriteAllText(P("README.md"), "[photo](assets/a.png)\r\n", Encoding.Unicode);
        var original = File.ReadAllBytes(P("README.md"));
        var operation = Executor.Execute(Plan(), true, false);
        Assert.Equal(new byte[] { 0xff, 0xfe }, File.ReadAllBytes(P("README.md"))[..2]);
        Executor.Undo(operation.Id);
        Assert.Equal(original, File.ReadAllBytes(P("README.md")));
    }

    [Fact] public void ExistingDestinationIsBlocked()
    {
        Write("assets/a", "a"); Write("archive/a", "b");
        Assert.False(Plan().CanExecute);
        Assert.Throws<IOException>(() => Executor.Execute(Plan(), true, true));
        Assert.Equal("b", File.ReadAllText(P("archive/a")));
    }

    [Fact] public void DestinationInsideSourceIsBlocked()
    {
        Write("assets/a", "a");
        Assert.False(Plan(destination: "assets/nested").CanExecute);
    }

    [Fact] public void ChangedReferenceRefusesMove()
    {
        Write("assets/a", "a"); Write("README.md", "[a](assets/a)");
        var plan = Plan();
        File.AppendAllText(P("README.md"), "new edits");
        Assert.Throws<IOException>(() => Executor.Execute(plan, true, true));
        Assert.True(Directory.Exists(P("assets")));
    }

    [Fact] public void ChangedSourceAndNewFilesRefuseMove()
    {
        Write("assets/a", "a");
        var plan = Plan();
        Write("assets/b", "new");
        Assert.Throws<IOException>(() => Executor.Execute(plan, true, true));
        Assert.True(File.Exists(P("assets/b")));
    }

    [Fact] public void UndoRefusesNewerEdits()
    {
        Write("assets/a", "a"); Write("README.md", "[a](assets/a)");
        var operation = Executor.Execute(Plan(), true, false);
        File.AppendAllText(P("README.md"), "new edits");
        Assert.Throws<IOException>(() => Executor.Undo(operation.Id));
        Assert.True(Directory.Exists(P("archive")));
        Assert.Contains("new edits", File.ReadAllText(P("README.md")));
    }

    [Fact] public void UndoRefusesAddedFileInsideDestination()
    {
        Write("assets/a", "a");
        var operation = Executor.Execute(Plan(), true, false);
        Write("archive/new", "keep");
        Assert.Throws<IOException>(() => Executor.Undo(operation.Id));
        Assert.True(File.Exists(P("archive/new")));
    }

    [Fact] public void UndoRefusesRecreatedSource()
    {
        Write("assets/a", "a");
        var operation = Executor.Execute(Plan(), true, false);
        Write("assets/new", "keep");
        Assert.Throws<IOException>(() => Executor.Undo(operation.Id));
    }

    [Fact] public void ReviewOnlyReferencesNeedAcknowledgment()
    {
        Write("assets/a.txt", "a");
        Write("project.csproj", "<Project><ItemGroup><Content Include=\"assets/a.txt\" /></ItemGroup></Project>");
        var plan = Plan();
        Assert.Equal(1, plan.AtRisk);
        Assert.Equal(0, plan.Repairable);
        Assert.Throws<InvalidOperationException>(() => Executor.Execute(plan, true, false));
        Executor.Execute(plan, true, true);
    }

    [Fact] public void InvalidJsonProducesVisibleWarning()
    {
        Write("assets/a", "a"); Write("bad.json", "{bad}");
        var plan = Plan();
        Assert.Single(plan.Warnings);
        Assert.Throws<InvalidOperationException>(() => Executor.Execute(plan, true, false));
    }

    [Fact] public void ScanRootsAreExplicit()
    {
        Write("assets/a", "a"); Write("outside/README.md", "[a](../assets/a)");
        var plan = new Planner().Analyze(new(P("assets"), P("archive"), [P("assets")]));
        Assert.Empty(plan.Impacts);
    }

    [Fact] public void CancelledAnalysisChangesNothing()
    {
        Write("assets/a", "a");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new Planner().Analyze(
            new(P("assets"), P("archive"), [root]), cancellation.Token));
        Assert.True(Directory.Exists(P("assets")));
    }

    [Fact] public void AFileCanBeRenamedAndRecovered()
    {
        Write("file.md", "content"); Write("README.md", "[file](file.md)");
        var plan = Plan("file.md", "renamed.md");
        var operation = Executor.Execute(plan, true, false);
        Assert.Contains("renamed.md", File.ReadAllText(P("README.md")));
        Executor.Undo(operation.Id);
        Assert.True(File.Exists(P("file.md")));
    }

    [Fact] public void NewEmptyDirectoryPreventsUndo()
    {
        Write("assets/a", "a");
        var operation = Executor.Execute(Plan(), true, false);
        Directory.CreateDirectory(P("archive/new-empty"));
        Assert.Throws<IOException>(() => Executor.Undo(operation.Id));
    }

    [Fact] public void HttpsMailAndAnchorsAreIgnored()
    {
        var refs = new MarkdownDetector().Detect(P("README.md"),
            "[web](https://example.com) [mail](mailto:a@example.com) [anchor](#here)").ToArray();
        Assert.Empty(refs);
    }

    [Fact] public void AnalysisDoesNotModifyFiles()
    {
        Write("assets/a", "a"); Write("README.md", "[a](assets/a)");
        var hash = Paths.Hash(P("README.md"));
        _ = Plan();
        Assert.Equal(hash, Paths.Hash(P("README.md")));
        Assert.True(Directory.Exists(P("assets")));
    }

    [Fact] public void UndoCanRecoverAnInterruptedMove()
    {
        Write("assets/a", "a"); Write("README.md", "[a](assets/a)");
        var store = new OperationStore(P("history"));
        var record = new MoveExecutor(store).Execute(Plan(), true, false);
        store.Save(record with { Status = "Needs recovery", Error = "Simulated interrupted checkpoint" });
        new MoveExecutor(store).Undo(record.Id);
        Assert.Equal("[a](assets/a)", File.ReadAllText(P("README.md")));
        Assert.True(Directory.Exists(P("assets")));
    }

    [Fact] public void NewReferenceFileInvalidatesPlan()
    {
        Write("assets/a", "a");
        var plan = Plan();
        Write("new.md", "[a](assets/a)");
        Assert.Throws<IOException>(() => Executor.Execute(plan, true, false));
        Assert.True(Directory.Exists(P("assets")));
    }

    [Fact] public void MarkdownCodeSamplesAreNotRewritten()
    {
        Write("assets/a.png", "a");
        Write("README.md", "\x60\x60\x60md\n[example](assets/a.png)\n\x60\x60\x60\n\x60[sample](assets/a.png)\x60\n[real](assets/a.png)");
        var plan = Plan();
        Assert.Single(plan.Impacts);
        Executor.Execute(plan, true, false);
        var text = File.ReadAllText(P("README.md"));
        Assert.Contains("[example](assets/a.png)", text);
        Assert.Contains("[sample](assets/a.png)", text);
        Assert.Contains("[real](archive/a.png)", text);
    }

    [Fact] public void MarkdownReferenceDefinitionsAreRepaired()
    {
        Write("assets/a b.png", "a");
        Write("README.md", "[photo][image]\n\n[image]: <assets/a%20b.png> \"Photo\"");
        Executor.Execute(Plan(), true, false);
        Assert.Contains("[image]: <archive/a%20b.png> \"Photo\"", File.ReadAllText(P("README.md")));
    }

    [Fact] public void AbsoluteJsonPathsAreRepaired()
    {
        Write("assets/a.png", "a");
        Write("settings.json", System.Text.Json.JsonSerializer.Serialize(new { path = P("assets/a.png") }));
        Executor.Execute(Plan(), true, false);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(P("settings.json")));
        Assert.True(Paths.Equal(P("archive/a.png"), doc.RootElement.GetProperty("path").GetString()!));
    }

    [Fact] public void MoveWithoutRepairsCanStillBeUndone()
    {
        Write("assets/a", "a"); Write("README.md", "[a](assets/a)");
        var operation = Executor.Execute(Plan(), false, true);
        Assert.Equal("[a](assets/a)", File.ReadAllText(P("README.md")));
        Executor.Undo(operation.Id);
        Assert.True(File.Exists(P("assets/a")));
    }

    [Fact] public void DamagedBackupBlocksRecovery()
    {
        Write("assets/a", "a"); Write("README.md", "[a](assets/a)");
        var store = new OperationStore(P("history"));
        var executor = new MoveExecutor(store);
        var operation = executor.Execute(Plan(), true, false);
        File.WriteAllText(Path.Combine(store.Folder(operation.Id), operation.Repairs.Single().BackupFile), "damaged");
        Assert.Throws<IOException>(() => executor.Undo(operation.Id));
        Assert.True(Directory.Exists(P("archive")));
    }

    [Fact] public void CancelledExecutionDoesNotMoveSource()
    {
        Write("assets/a", "a");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Executor.Execute(Plan(), true, false, cancellation.Token));
        Assert.True(Directory.Exists(P("assets")));
    }

    [Fact] public void OperationCannotBeUndoneTwice()
    {
        Write("assets/a", "a");
        var executor = Executor;
        var operation = executor.Execute(Plan(), true, false);
        executor.Undo(operation.Id);
        Assert.Throws<IOException>(() => executor.Undo(operation.Id));
    }

    [Fact] public void HistoryIndexCanBeRebuilt()
    {
        if (!OperatingSystem.IsWindows()) return;
        Write("assets/a", "a");
        var store = new OperationStore(P("history"));
        var operation = new MoveExecutor(store).Execute(Plan(), true, false);
        using (var index = new HistoryIndex(P("history.db"))) index.Rebuild(store.List());
        Assert.True(File.Exists(P("history.db")));
        Assert.Equal("Completed", operation.Status);
    }

    [Fact] public void JsonVersionStringsAreNeverTreatedAsPaths()
    {
        Write("config/settings.json", "{\"version\":\"1.2.3\", \"label\":\"photo.png\"}");
        Directory.CreateDirectory(P("nested"));
        var plan = Plan("config/settings.json", "nested/settings.json");
        Assert.Empty(plan.Impacts);
        Executor.Execute(plan, true, false);
        Assert.Equal("{\"version\":\"1.2.3\", \"label\":\"photo.png\"}", File.ReadAllText(P("nested/settings.json")));
    }

    [Fact] public void MissingPathsAreReviewOnly()
    {
        Write("assets/notes.md", "[missing](../missing.png)");
        Directory.CreateDirectory(P("nested"));
        var plan = Plan(destination: "nested/assets");
        Assert.Equal(1, plan.AtRisk);
        Assert.Equal(0, plan.Repairable);
        Assert.Throws<InvalidOperationException>(() => Executor.Execute(plan, true, false));
    }

    [Fact] public void IndentedCodeIsNotAReference()
    {
        Write("assets/a.png", "a");
        Write("README.md", "    [code](assets/a.png)\n[real](assets/a.png)");
        var plan = Plan();
        Assert.Single(plan.Impacts);
    }

    [Fact] public void WindowsShortcutTargetsAreReviewOnly()
    {
        if (!OperatingSystem.IsWindows()) return;
        Write("assets/a.txt", "a");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(P("example.lnk"));
            ((dynamic)shortcut).TargetPath = P("assets/a.txt");
            ((dynamic)shortcut).Save();
            var plan = Plan();
            Assert.Contains(plan.Impacts, i => i.Reference.Kind == ReferenceKind.Shortcut && i.Breaks && !i.Reference.CanRepair);
        }
        finally
        {
            if (shortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}




