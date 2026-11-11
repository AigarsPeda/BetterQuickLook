using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterQuickLook;
using WpfImage = System.Windows.Controls.Image;
using WpfButton = System.Windows.Controls.Button;

internal static partial class Program
{
    private static string WorkspaceFixture()
    {
        var root = Directory.CreateDirectory(Path.Combine(_folder, "workspace")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "CustomerPortal"));
        var path = Path.Combine(root, "CustomerPortall.code-workspace");
        File.WriteAllText(path, """
        { // Workspace files allow comments and trailing commas.
          "folders": [
            { "name": "Customer portal", "path": "CustomerPortal" },
            { "path": "missing folder" },
            { "name": "Remote", "uri": "vscode-remote://ssh-remote+sample/work" },
            { "name": "Network", "path": "\\\\localhost\\unavailable-share" },
          ],
          "settings": { "editor.tabSize": 2, "files.exclude": {} },
          "tasks": { "tasks": [ { "label": "Build" } ] },
          "launch": { "configurations": [ { "name": "Debug" } ] },
          "extensions": { "recommendations": [ "ms-dotnettools.csharp" ] },
        }
        """);
        return path;
    }

    private static void WorkspaceFiles()
    {
        var path = WorkspaceFixture();
        using (var data = WorkspacePreview.Read(path))
        {
            Check(data.Json.Document is not null && data.Folders.Count == 4 && data.FolderCount == 4, "JSONC workspace parses");
            Check(data.Folders[0].Name == "Customer portal" && data.Folders[0].LocalPath == Path.Combine(Path.GetDirectoryName(path)!, "CustomerPortal"),
                "relative folder paths resolve against the workspace, with custom names");
            Check(data.Folders[2].LocalPath is null && data.Folders[2].Notice!.Contains("Remote"), "remote URI is display-only");
            Check(data.Folders[3].Notice!.Contains("Network"), "UNC paths are not checked automatically");
            Check(data.Settings == 2 && data.Tasks == 1 && data.LaunchConfigurations == 1 && data.Extensions.Single() == "ms-dotnettools.csharp",
                "configuration summary counts actual sections");
        }
        var uri = new Uri(Path.GetDirectoryName(path)! + "\\").AbsoluteUri;
        File.WriteAllText(path, JsonSerializer.Serialize(new { folders = new object[] { new { uri, name = "Rīga 😀" }, new { path = "${env:HOME}" }, 12, new { path = "bad\0path" } } }));
        using (var data = WorkspacePreview.Read(path))
        {
            Check(data.Folders[0].LocalPath is not null && data.Folders[0].Name == "Rīga 😀", "file URI and Unicode names work");
            Check(data.Folders.Skip(1).All(folder => folder.LocalPath is null && folder.Notice is not null), "invalid entries and variables stay inert");
        }
        foreach (var content in new[] { "[]", "{\"folders\":null}", "{broken" })
        {
            File.WriteAllText(path, content);
            using var data = WorkspacePreview.Read(path);
            Check(data.Notice is not null, "invalid workspace shape gives an explanation");
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { folders = Enumerable.Repeat(new { path = "." }, 150),
            extensions = new { recommendations = Enumerable.Repeat("example.extension", 40) } }));
        using (var data = WorkspacePreview.Read(path))
            Check(data.FolderCount == 150 && data.Folders.Count == WorkspacePreview.MaxFolders && data.Extensions.Length == 30,
                "large folder and extension lists have bounded summaries");
        File.WriteAllText(path, new string(' ', JsonPreviewReader.MaxFileBytes + 1));
        using (var data = WorkspacePreview.Read(path))
            Check(data.Json.Document is null && data.Json.Text.Length <= JsonPreviewReader.MaxShownChars && data.Notice!.Contains("Large file"),
                "oversized workspaces use the existing bounded JSON fallback");
        var explorer = WorkspacePreviewView.ExplorerStartInfo(Path.Combine(_folder, "space Rīga"));
        Check(explorer.ArgumentList.Count == 1 && explorer.ArgumentList[0] == Path.Combine(_folder, "space Rīga"), "Explorer receives one path argument");
    }

    private static string CreateShortcut(string name, string target, string arguments = "")
    {
        var path = Path.Combine(_folder, name + ".lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(path);
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).Arguments = arguments;
            ((dynamic)shortcut).Description = "Preview test • Rīga";
            ((dynamic)shortcut).WorkingDirectory = _folder;
            ((dynamic)shortcut).Save();
            return path;
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void ShortcutFiles()
    {
        var file = Path.Combine(_folder, "target Rīga.json");
        File.WriteAllText(file, "{}");
        var link = CreateShortcut("file", file);
        var before = File.ReadAllBytes(link);
        var result = PreviewWork.RunAsync(() =>
        {
            Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "shortcut reader runs on STA");
            return ShortcutPreview.Read(link);
        }, CancellationToken.None, sta: true).GetAwaiter().GetResult();
        Check(result.Status == ShortcutStatus.File && result.Target == file && result.Description.Contains("Rīga"), "native Shell target and metadata read");
        Check(before.SequenceEqual(File.ReadAllBytes(link)), "preview must not rewrite the shortcut");
        using (File.Open(link, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(ShortcutPreview.Read(CreateShortcut("folder", _folder)).Status == ShortcutStatus.Folder, "folder target recognized");
        Check(ShortcutPreview.Read(CreateShortcut("missing", file + ".missing")).Status == ShortcutStatus.Missing, "broken target recognized");
        var corrupt = Path.Combine(_folder, "corrupt.lnk");
        File.WriteAllText(corrupt, "not a shortcut");
        Check(ShortcutPreview.Read(corrupt).Status == ShortcutStatus.Invalid, "corrupt shortcut handled");
        // Windows refuses to save shortcut chains. Exercise our traversal guards
        // with saved metadata; ordinary targets above still use real Shell links.
        var links = new Dictionary<string, ShortcutPreview.SavedLink>(StringComparer.OrdinalIgnoreCase);
        string AddLink(string name, string target)
        {
            var path = Path.Combine(_folder, name + ".lnk");
            File.WriteAllText(path, "fixture");
            links[path] = new(target, "", "", "");
            return path;
        }
        var loopA = AddLink("loop-a", Path.Combine(_folder, "loop-b.lnk"));
        AddLink("loop-b", loopA);
        Check(ShortcutPreview.Read(loopA, path => links[path]).Status == ShortcutStatus.Cycle, "shortcut cycle stops");
        for (var i = 0; i <= ShortcutPreview.MaxLinks; i++)
            AddLink("deep-" + i, i == ShortcutPreview.MaxLinks ? file : Path.Combine(_folder, $"deep-{i + 1}.lnk"));
        Check(ShortcutPreview.Read(Path.Combine(_folder, "deep-0.lnk"), path => links[path]).Status == ShortcutStatus.TooDeep, "long chains are bounded");
        var endLink = AddLink("chain-end", file);
        var nested = AddLink("nested", endLink);
        Check(ShortcutPreview.Read(nested, path => links[path]).Target == file, "short chain reaches its file");
        var network = CreateShortcut("network", @"\\localhost\BetterQuickLook-missing-share\image.png");
        Check(ShortcutPreview.Read(network).Status == ShortcutStatus.Network, "network targets stay unconnected");
        var script = Path.Combine(_folder, "must-not-run.cmd");
        var marker = Path.Combine(_folder, "unexpected-execution.txt");
        File.WriteAllText(script, "@echo executed > \"" + marker + "\"");
        var command = ShortcutPreview.Read(CreateShortcut("command", script, "--example"));
        Check(command.Arguments == "--example" && command.WorkingDirectory == _folder && !File.Exists(marker), "reading a command shortcut never executes it");
    }

    private static void LinkedPreviewWindows(PreviewWindow preview)
    {
        var workspace = WorkspaceFixture();
        preview.ShowFile(workspace);
        Until(() => FindVisual<WorkspacePreviewView>(preview, _ => true) is not null, "workspace summary");
        var view = FindVisual<WorkspacePreviewView>(preview, _ => true)!;
        Until(() => view.AvailabilityTask.IsCompleted, "folder checks");
        view.AvailabilityTask.GetAwaiter().GetResult();
        Check(ContainsText(view, "Customer portal") && ContainsText(view, "Available") && ContainsText(view, "Missing or inaccessible"), "folder cards and availability shown");
        Check(FindVisual<JsonPreviewView>(view, _ => true) is null, "JSON rendering deferred until requested");
        Check(FindVisual<WpfButton>(view, button => Equals(button.Content, "Open folder")) is not null &&
              FindVisual<WpfButton>(view, button => Equals(button.Content, "Copy path")) is not null, "workspace folder actions present");
        view.JsonButton.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
        Check(FindVisual<JsonPreviewView>(view, _ => true) is not null, "JSON tab reuses the existing preview");
        preview.ShowFile(CreateShortcut("workspace-view", workspace));
        Until(() => FindVisual<WorkspacePreviewView>(preview, _ => true) is not null, "workspace through shortcut");
        Check(ContainsText(preview, "Target: " + workspace), "shortcut target displayed above content");
        var image = Path.Combine(_folder, "shortcut-image.png");
        using (var bitmap = new Bitmap(40, 30)) bitmap.Save(image, ImageFormat.Png);
        preview.ShowFile(CreateShortcut("image-view", image));
        Until(() => FindVisual<WpfImage>(preview, item => item.Source is not null) is not null, "image through shortcut");
        Check(ContainsText(preview, "image-view.lnk") && ContainsText(preview, image) && preview.CanOpenFile, "shortcut name, image, and target app action shown");
        preview.ShowFile(CreateShortcut("folder-view", Path.GetDirectoryName(workspace)!));
        Until(() => ContainsText(preview, "LOCATION"), "folder through shortcut");
        Check(preview.OpenButtonLabel == "Open shortcut", "folder shortcut can open in Explorer");
        preview.ShowFile(CreateShortcut("missing-view", image + ".missing"));
        Until(() => ContainsText(preview, "target is missing"), "broken shortcut status");
        Check(!preview.CanOpenFile, "broken shortcut has no stale launch action");
        var script = Path.Combine(_folder, "preview-must-not-run.cmd");
        var marker = Path.Combine(_folder, "preview-ran.txt");
        File.WriteAllText(script, "@echo executed > \"" + marker + "\"");
        preview.ShowFile(CreateShortcut("command-view", script, "test-argument"));
        Until(() => preview.CanOpenFile && ContainsText(preview, "Arguments: test-argument"), "command shortcut metadata");
        Check(!File.Exists(marker) && ContainsText(preview, "Arguments: test-argument"), "showing a program shortcut does not launch it");
        preview.ShowFile(workspace);
        preview.ShowFile(CreateShortcut("rapid-view", image));
        preview.ShowFile(image);
        Until(() => FindVisual<WpfImage>(preview, item => item.Source is not null) is not null, "latest selection after rapid changes");
        Check(!ContainsText(preview, "Target:") && FindVisual<WorkspacePreviewView>(preview, _ => true) is null, "stale workspace or shortcut cannot replace current image");
        preview.ShowFile(workspace);
        preview.Hide();
        PumpDispatcher(TimeSpan.FromMilliseconds(150));
        Check(!preview.IsVisible, "workspace completion cannot reopen a closed preview");
    }

    private static void LinkedPreviewTestSession(string? renderPath = null)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var preview = new PreviewWindow(Path.Combine(_folder, "linked-settings.json"));
        try
        {
            Run("Workspace and shortcut windows, actions, cancellation and safe command previews", () => LinkedPreviewWindows(preview));
            if (renderPath is not null)
            {
                preview.ShowFile(WorkspaceFixture());
                Until(() => FindVisual<WorkspacePreviewView>(preview, _ => true)?.AvailabilityTask.IsCompleted == true, "render workspace");
                preview.UpdateLayout();
                var image = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(preview);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(renderPath); encoder.Save(output);
            }
        }
        finally { preview.ClosePermanently(); app.Shutdown(); }
    }
}
