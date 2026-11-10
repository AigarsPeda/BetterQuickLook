using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using System.Windows.Threading;
using BetterQuickLook;

internal static class Program
{
    private static string _folder = "";
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BetterQuickLook-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        try
        {
            Run("Desktop and Explorer shortcut rules", ShortcutRules);
            Run("Active Explorer tab selection", ActiveExplorerTab);
            Run("PNG and JPEG decode at preview size", Images);
            Run("Corrupt image rejection", CorruptImage);
            Run("Image editor preference and launch arguments", ImageEditorChoice);
            Run("Windows Open With image programs", ImagePrograms);
            Run("Word text and empty document", Word);
            Run("Excel shared, inline, and numeric cells", Excel);
            Run("Missing and malformed document parts", InvalidDocuments);
            Run("JSON formatting, tree, and edge cases", JsonFiles);
            Run("Folder size and scan edge cases", Folders);
            if (!Environment.GetCommandLineArgs().Contains("--no-ui"))
            {
                Run("Desktop Shell selection", DesktopShell);
                Run("Preview remains visible after focus changes", WindowLifecycle);
                if (Environment.GetCommandLineArgs().Contains("--open-with-smoke"))
                    Run("Open With launches Paint", OpenWithSmoke);
            }
            Console.WriteLine($"{_passed} tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { Directory.Delete(_folder, true); }
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void ShortcutRules()
    {
        Check(KeyboardHook.AcceptsShellSpace("Progman", ""), "Desktop should accept Space");
        Check(KeyboardHook.AcceptsShellSpace("WorkerW", ""), "Desktop WorkerW should accept Space");
        Check(KeyboardHook.AcceptsShellSpace("CabinetWClass", "DirectUIHWND"), "Explorer list should accept Space");
        Check(KeyboardHook.AcceptsShellSpace("ExploreWClass", "SysListView32"), "Classic Explorer list should accept Space");
        Check(!KeyboardHook.AcceptsShellSpace("CabinetWClass", "Edit"), "Explorer text entry must keep Space");
        Check(!KeyboardHook.AcceptsShellSpace("Chrome_WidgetWin_1", "DirectUIHWND"), "Other apps must keep Space");
    }

    private static void ActiveExplorerTab()
    {
        var entries = new List<(string Name, string? Path)> { ("GeoWeb2", "old.png"), ("work", "new.png") };
        Check(ExplorerSelection.ChooseSelection("work and 1 more tab - File Explorer", entries) == "new.png", "active tab wins");
        Check(ExplorerSelection.ChooseSelection("GeoWeb2 and 1 more tab - File Explorer", entries) == "old.png", "switching tabs works");
        Check(ExplorerSelection.ChooseSelection("work - File Explorer", [("work", null), ("other", "old.png")]) is null,
            "no selection in the active tab must not use an inactive tab");
        Check(ExplorerSelection.ChooseSelection("unknown", [("only", "single.png")]) == "single.png", "single tab fallback works");
        Check(ExplorerSelection.ChooseSelection("unknown", entries) is null, "ambiguous tabs must not guess");
        Check(ExplorerSelection.ChooseSelection("work stuff and 1 more tab", [("work", "wrong.png"), ("work stuff", "right.png")]) == "right.png",
            "longest matching tab name wins");
    }

    private static void Images()
    {
        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg })
        {
            var path = Path.Combine(_folder, "large." + (format.Equals(ImageFormat.Png) ? "png" : "jpg"));
            using (var image = new Bitmap(3000, 2000)) image.Save(path, format);
            var preview = PreviewWindow.DecodeImage(path);
            Check(Math.Max(preview.PixelWidth, preview.PixelHeight) <= 1800, "large image should be decoded at preview size");
            Check(preview.PixelWidth > 0 && preview.PixelHeight > 0, "decoded image should have pixels");
        }
    }

    private static void CorruptImage()
    {
        var path = Path.Combine(_folder, "broken.png");
        File.WriteAllText(path, "not an image");
        ExpectFailure(() => PreviewWindow.DecodeImage(path), "corrupt image must fail cleanly");
    }

    private static void ImageEditorChoice()
    {
        var settingsPath = Path.Combine(_folder, "editor preference", "settings.json");
        var editor = Environment.ProcessPath!;
        var image = Path.Combine(_folder, "image with spaces ä.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        Check(ImageEditor.Load(settingsPath) is null, "no editor should be selected initially");
        ImageEditor.Save(editor, settingsPath);
        Check(ImageEditor.Load(settingsPath) == editor, "editor choice should persist");
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(new { ImageEditorPath = editor }));
        Check(ImageEditor.LoadSelection(settingsPath)?.EditorPath == editor, "older editor settings should still load");
        ImageEditor.SaveHandler("Example.ImageHandler", "Example editor", settingsPath);
        var chosenHandler = ImageEditor.LoadSelection(settingsPath);
        Check(chosenHandler?.HandlerName == "Example.ImageHandler" && chosenHandler.DisplayName == "Example editor" &&
              chosenHandler.EditorPath is null, "registered program choice should persist without an executable path");
        ImageEditor.Save(editor, settingsPath);
        var start = ImageEditor.CreateStartInfo(editor, image);
        Check(start.FileName == editor && start.ArgumentList.Count == 1 && start.ArgumentList[0] == image &&
              start.UseShellExecute, "image path should be passed as one argument, including spaces and Unicode");
        File.WriteAllText(settingsPath, "{invalid");
        Check(ImageEditor.Load(settingsPath) is null, "damaged settings should prompt for an editor again");
        ExpectFailure(() => ImageEditor.Save(Path.Combine(_folder, "missing.exe"), settingsPath),
            "missing editor should not be saved");
        ExpectFailure(() => ImageEditor.CreateStartInfo(editor, Path.Combine(_folder, "missing.png")),
            "missing image should not be launched");
    }

    private static void ImagePrograms()
    {
        foreach (var extension in new[] { ".png", ".jpg" })
        {
            var loading = ShellImageHandlers.ListAsync(extension);
            Check(ReferenceEquals(loading, ShellImageHandlers.ListAsync(extension)), "program lists should be reused per image type");
            var handlers = loading.GetAwaiter().GetResult();
            Check(handlers.Count > 0, $"Windows should list programs for {extension}");
            Check(handlers.All(handler => !string.IsNullOrWhiteSpace(handler.Name) &&
                                          !string.IsNullOrWhiteSpace(handler.DisplayName)),
                "Open With programs should have identifiers and display names");
            Check(handlers.Select(handler => handler.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == handlers.Count,
                "Open With menu should not repeat a program");
        }
        Check(ShellImageHandlers.Matches(new ShellImageHandlers.Handler(@"C:\Apps\Paint_2\mspaint.exe", "Paint", true),
                @"C:\Apps\Paint_1\mspaint.exe", "Paint"), "updated packaged apps should keep the saved choice");
        Check(!ShellImageHandlers.Matches(new ShellImageHandlers.Handler(@"C:\Apps\Other\mspaint.exe", "Other", true),
                @"C:\Apps\Paint\mspaint.exe", "Paint"), "a different program must not inherit the saved choice");
    }

    private static void OpenWithSmoke()
    {
        var paint = ShellImageHandlers.List(".png").First(handler =>
            handler.Name.Contains("mspaint.exe", StringComparison.OrdinalIgnoreCase));
        var image = Path.Combine(_folder, "open-with-smoke.png");
        using (var bitmap = new Bitmap(16, 16)) bitmap.Save(image, ImageFormat.Png);
        var existing = System.Diagnostics.Process.GetProcessesByName("mspaint").Select(process => process.Id).ToHashSet();
        Check(existing.Count == 0, "close Paint before running the optional launch smoke test");
        try
        {
            ShellImageHandlers.Open(".png", paint.Name, paint.DisplayName, image);
            Thread.Sleep(1500);
        }
        finally
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("mspaint"))
                if (!existing.Contains(process.Id)) process.Kill();
        }
    }

    private static void Word()
    {
        var path = Path.Combine(_folder, "sample.docx");
        CreateZip(path, ("word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body>" +
            "<w:p><w:r><w:t>Sveiki</w:t></w:r></w:p><w:p><w:r><w:t>World</w:t></w:r></w:p>" +
            "</w:body></w:document>"));
        var text = DocumentPreview.Read(path);
        Check(text.Contains("Sveiki") && text.Contains("World"), "Word text should be extracted");
        Check(text.IndexOf("Sveiki", StringComparison.Ordinal) < text.IndexOf("World", StringComparison.Ordinal), "paragraph order should be preserved");

        var empty = Path.Combine(_folder, "empty.docx");
        CreateZip(empty, ("word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body/></w:document>"));
        Check(DocumentPreview.Read(empty).Contains("no readable text"), "empty Word document should have a clear fallback");
    }

    private static void Excel()
    {
        var path = Path.Combine(_folder, "sample.xlsx");
        CreateZip(path,
            ("xl/sharedStrings.xml", "<sst><si><t>Alpha</t></si><si><t>Beta</t></si></sst>"),
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row r='1'>" +
             "<c r='A1' t='s'><v>0</v></c><c r='B1' t='s'><v>1</v></c>" +
             "<c r='C1' t='inlineStr'><is><t>Inline</t></is></c><c r='D1'><v>42</v></c>" +
             "</row></sheetData></worksheet>"));
        Check(DocumentPreview.Read(path).Contains("Alpha\tBeta\tInline\t42"), "Excel values should be read in column order");
    }

    private static void InvalidDocuments()
    {
        var missing = Path.Combine(_folder, "missing.docx");
        CreateZip(missing, ("other.xml", "<empty/>"));
        ExpectFailure(() => DocumentPreview.Read(missing), "missing document part should fail");
        var malformed = Path.Combine(_folder, "malformed.xlsx");
        File.WriteAllText(malformed, "not a ZIP archive");
        ExpectFailure(() => DocumentPreview.Read(malformed), "malformed workbook should fail");
    }

    private static void DesktopShell()
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        object windows = ((dynamic)shell).Windows();
        var selected = ExplorerSelection.GetDesktopSelection(windows);
        Check(selected is null || File.Exists(selected) || Directory.Exists(selected),
            "Desktop selection should resolve to a real file or folder");
    }

    private static void Folders()
    {
        var root = Path.Combine(_folder, "folder-scan");
        Directory.CreateDirectory(root);
        Check(FolderPreview.Scan(root, CancellationToken.None) == new FolderScanResult(0, 0, 0, 0, false),
            "empty folder should have zero size and items");

        File.WriteAllBytes(Path.Combine(root, "empty.txt"), []);
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllBytes(Path.Combine(nested.FullName, "data.bin"), new byte[2048]);
        var result = FolderPreview.Scan(root, CancellationToken.None);
        Check(result.Bytes == 2048 && result.Files == 2 && result.Folders == 1 && !result.IsPartial,
            "nested files and zero-byte files should count correctly");

        var limited = FolderPreview.Scan(root, CancellationToken.None, maxEntries: 1);
        Check(limited.ReachedLimit && limited.IsPartial, "large scans should stop at the item limit");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { FolderPreview.Scan(root, canceled.Token); throw new Exception("canceled scan continued"); }
        catch (OperationCanceledException) { }
        ExpectFailure(() => FolderPreview.Scan(Path.Combine(root, "missing"), CancellationToken.None),
            "missing folders should fail clearly");
    }

    private static void JsonFiles()
    {
        var valid = Path.Combine(_folder, "sample.json");
        File.WriteAllText(valid, "{\"name\":\"Rīga\",\"items\":[1,true,null,{\"nested\":\"yes\"}]}");
        using (var data = JsonPreviewReader.Read(valid))
        {
            Check(data.Document is not null && data.Notice is null, "valid JSON should parse");
            Check(data.Text.Contains("\n") && data.Text.Contains("Rīga"), "JSON should be formatted and preserve Unicode");
            var view = new JsonPreviewView(valid, data);
            var paragraph = (Paragraph)view.FormattedText.Document.Blocks.FirstBlock!;
            Check(paragraph.Inlines.OfType<Run>().Any(run => run.Text == "\"name\""),
                "property names should receive syntax highlighting");
            var root = (System.Windows.Controls.TreeViewItem)view.Tree.Items[0];
            Check(root.Items.Count == 1, "tree children should be deferred until expansion");
            root.IsExpanded = true;
            Check(root.Items.Count == 2, "expanding an object should show its properties");
        }

        var jsonc = Path.Combine(_folder, "comments.jsonc");
        File.WriteAllText(jsonc, "{ // note\n \"value\": 1, }");
        using (var data = JsonPreviewReader.Read(jsonc))
            Check(data.Document is not null && data.Text.Contains("\"value\""), "JSONC comments and trailing commas should be supported");

        var invalid = Path.Combine(_folder, "broken.json");
        File.WriteAllText(invalid, "{\"ok\": 1,\n\"bad\": }");
        using (var data = JsonPreviewReader.Read(invalid))
            Check(data.Document is null && data.Notice?.Contains("line 2") == true,
                "invalid JSON should show the error line");

        var large = Path.Combine(_folder, "large.json");
        File.WriteAllText(large, "{\"data\":\"" + new string('x', JsonPreviewReader.MaxFileBytes) + "\"}");
        using (var data = JsonPreviewReader.Read(large))
            Check(data.Document is null && data.Text.Length <= JsonPreviewReader.MaxShownChars &&
                  data.Notice?.StartsWith("Large file") == true,
                "oversized JSON should use a bounded text sample");

        var array = Path.Combine(_folder, "array.json");
        File.WriteAllText(array, "[" + string.Join(',', Enumerable.Repeat("0", 501)) + "]");
        using (var data = JsonPreviewReader.Read(array))
        {
            var view = new JsonPreviewView(array, data);
            var root = (System.Windows.Controls.TreeViewItem)view.Tree.Items[0];
            root.IsExpanded = true;
            Check(root.Items.Count == 501, "large tree nodes should show 500 entries and a limit notice");
        }
    }

    private static void WindowLifecycle()
    {
        var path = Path.Combine(_folder, "visible.png");
        using (var image = new Bitmap(20, 20)) image.Save(path, ImageFormat.Png);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/BetterQuickLook;component/ScrollBarTheme.xaml", UriKind.Relative)
        });
        var preview = new PreviewWindow();
        var other = new Window { Width = 100, Height = 100, ShowInTaskbar = false };
        try
        {
            preview.ShowFile(path);
            Check(preview.IsVisible, "preview should open");
            Check(preview.CanEditImage, "image preview should show editor controls");
            preview.ChangeProgramButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            for (var attempt = 0; attempt < 10 && preview.EditorMenu?.Items.Count <= 2; attempt++)
                PumpDispatcher(TimeSpan.FromMilliseconds(100));
            var editorMenu = preview.EditorMenu;
            Check(editorMenu?.IsOpen == true && editorMenu.Items.OfType<MenuItem>()
                .Any(item => Equals(item.Header, ShellImageHandlers.ListAsync(".png").GetAwaiter().GetResult()[0].DisplayName)),
                "Change program should show Windows Open With choices");
            editorMenu!.IsOpen = false;
            Check(preview.Topmost, "preview should appear above other windows");
            other.Show();
            other.Activate();
            PumpDispatcher(TimeSpan.FromMilliseconds(300));
            Check(preview.IsVisible, "preview should not disappear when focus changes");
            preview.Hide();
            Check(!preview.IsVisible, "preview should close on request");
            preview.ShowFile(path);
            Check(preview.IsVisible && preview.Topmost, "preview should reopen above other windows");
            preview.Close();
            Check(!preview.IsVisible, "the title bar close button should hide the preview");
            preview.ShowFile(path);
            Check(preview.IsVisible && preview.Topmost, "preview should reopen after the title bar close button");
            var folder = Directory.CreateDirectory(Path.Combine(_folder, "folder-window"));
            preview.Hide();
            preview.ShowFile(folder.FullName);
            PumpDispatcher(TimeSpan.FromMilliseconds(150));
            Check(!preview.CanEditImage, "folder preview should not show editor controls");
            Check(ContainsText(preview, "LOCATION") && ContainsText(preview, folder.FullName),
                "folder preview should show a labeled location");
            Check(ContainsText(preview, "SIZE") && ContainsText(preview, "CONTENTS"),
                "folder preview should show size and item count fields");
            var json = Path.Combine(_folder, "window.json");
            File.WriteAllText(json, "{\"items\":[" + string.Join(',', Enumerable.Range(0, 300).Select(i => $"{{\"number\":{i}}}")) + "]}");
            preview.Hide();
            preview.ShowFile(json);
            Check(!preview.CanEditImage, "JSON preview should not show editor controls");
            for (var attempt = 0; attempt < 10 && !ContainsText(preview, json); attempt++)
                PumpDispatcher(TimeSpan.FromMilliseconds(100));
            Check(ContainsText(preview, json), "JSON files should open the JSON preview view");
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            var scrollBar = FindVisual<WpfScrollBar>(preview, bar => bar.Orientation == System.Windows.Controls.Orientation.Vertical && bar.IsVisible);
            Check(scrollBar is not null && scrollBar.ActualWidth <= 12, "JSON preview should use the narrow themed scrollbar");
            var track = scrollBar!.Template.FindName("PART_Track", scrollBar) as Track;
            Check(track is not null && scrollBar.Maximum > scrollBar.Minimum, "themed scrollbar must keep its draggable track");
            var thumb = FindVisual<Border>(track!.Thumb, border => border.Background is SolidColorBrush);
            Check(thumb?.Background is SolidColorBrush brush && brush.Color == System.Windows.Media.Color.FromRgb(98, 113, 133),
                "JSON preview scrollbar thumb should use the dark theme color");
        }
        finally
        {
            other.Close();
            preview.ClosePermanently();
            app.Shutdown();
        }
    }

    private static bool ContainsText(DependencyObject root, string value)
    {
        if (root is TextBlock label && label.Text.Contains(value, StringComparison.Ordinal)) return true;
        if (root is System.Windows.Controls.TextBox box && box.Text.Contains(value, StringComparison.Ordinal)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (ContainsText(VisualTreeHelper.GetChild(root, i), value)) return true;
        return false;
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> matches) where T : DependencyObject
    {
        if (root is T item && matches(item)) return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual(VisualTreeHelper.GetChild(root, i), matches) is { } child) return child;
        return null;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open());
            writer.Write(entry.Content);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch { return; }
        throw new Exception(message);
    }
}
