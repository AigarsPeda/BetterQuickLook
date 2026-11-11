using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfImage = System.Windows.Controls.Image;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using System.Windows.Controls.Primitives;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using System.Windows.Threading;
using BetterQuickLook;

internal static partial class Program
{
    private static string _folder = "";
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--launch-benchmark")) return LaunchBenchmark(arguments[^3], arguments[^2], arguments[^1]);
        if (arguments.Contains("--tray-lifecycle")) return TrayLifecycle(arguments[^1]);
        if (arguments.Contains("--benchmark-app-picker")) return BenchmarkAppPicker(arguments[^1]);
        if (arguments.Contains("--app-picker-sample")) return AppPickerSample(arguments[^3], arguments[^2], arguments[^1]);
        if (arguments.Contains("--benchmark")) return Performance.Run(arguments);
        _folder = Path.Combine(Path.GetTempPath(), "BetterQuickLook-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        try
        {
            Run("Desktop and Explorer shortcut rules", ShortcutRules);
            Run("Active Explorer tab selection", ActiveExplorerTab);
            Run("PNG and JPEG decode at preview size", Images);
            Run("Corrupt image rejection", CorruptImage);
            Run("Preview work cancellation, concurrency, and resource disposal", PreviewWorkLimits);
            Run("JSON display text and syntax colors", JsonDisplay);
            Run("Preferred apps by file type and launch arguments", PreferredApps);
            Run("Direct editor delivery, timeout and duplicate-open protection", ProgramLaunchDelivery);
            Run("Windows Open With programs", FilePrograms);
            Run("Program icon cache reuse and file replacement", ProgramIconCache);
            Run("Word text and empty document", Word);
            Run("Document text limits and Unicode boundaries", DocumentTextLimits);
            Run("Excel shared, inline, and numeric cells", Excel);
            Run("Excel selective shared text, sparse cells, and preview limits", ExcelEdges);
            Run("Missing and malformed document parts", InvalidDocuments);
            Run("JSON formatting, tree, and edge cases", JsonFiles);
            Run("Folder size and scan edge cases", Folders);
            Run("Folder boundaries, cancellation, hidden and Unicode files", () => EdgeCases.Folders(_folder));
            Run("JSON reference formatting, long strings and excessive nesting", () => EdgeCases.JsonFormatting(_folder));
            Run("Large JSON Unicode boundary", () => EdgeCases.UnicodeSample(_folder));
            Run("Portrait image dimensions and file handle release", () => EdgeCases.ImageLifetime(_folder));
            if (!Environment.GetCommandLineArgs().Contains("--no-ui"))
            {
                Run("Desktop Shell selection", DesktopShell);
                Run("Preview remains visible after focus changes", WindowLifecycle);
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

    private static void PreferredApps()
    {
        var settingsPath = Path.Combine(_folder, "preferred apps", "settings.json");
        var program = Environment.ProcessPath!;
        var file = Path.Combine(_folder, "file with spaces ä.png");
        File.WriteAllBytes(file, [1, 2, 3]);
        Check(FileOpener.LoadSelection(".png", settingsPath) is null, "no app should be chosen initially");
        FileOpener.SaveExecutable(".PNG", program, settingsPath);
        Check(FileOpener.LoadSelection(".jpg", settingsPath)?.ExecutablePath == program,
            "one image choice should cover other image formats");
        Check(FileOpener.LoadSelection(".json", settingsPath) is null, "image choice must not apply to JSON");
        FileOpener.SaveHandler(".json", "Example.Code", "VS Code", settingsPath);
        Check(FileOpener.LoadSelection(".jsonc", settingsPath)?.DisplayName == "VS Code",
            "JSON and JSONC should share a preferred app");
        FileOpener.SaveHandler(".docx", "Example.Word", "Word", settingsPath);
        Check(FileOpener.LoadSelection(".doc", settingsPath)?.HandlerName == "Example.Word",
            "Word document formats should share a preferred app");
        FileOpener.SaveHandler(".xlsx", "Example.Excel", "Excel", settingsPath);
        Check(FileOpener.LoadSelection(".xls", settingsPath)?.DisplayName == "Excel" &&
              FileOpener.LoadSelection(".json", settingsPath)?.DisplayName == "VS Code",
            "saving another file type must preserve earlier choices");
        var start = FileOpener.CreateStartInfo(program, file);
        Check(start.FileName == program && start.ArgumentList.Count == 1 && start.ArgumentList[0] == file &&
              start.UseShellExecute, "file path should be passed as one argument, including spaces and Unicode");
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(new { ImageEditorPath = program }));
        Check(FileOpener.LoadSelection(".png", settingsPath)?.ExecutablePath == program &&
              FileOpener.LoadSelection(".json", settingsPath) is null, "older image settings should migrate only to images");
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(new { HandlerName = "Old.Paint", DisplayName = "Paint" }));
        Check(FileOpener.LoadSelection(".jpg", settingsPath)?.HandlerName == "Old.Paint",
            "older registered image choices should still load");
        File.WriteAllText(settingsPath, "{invalid");
        Check(FileOpener.LoadSelection(".png", settingsPath) is null, "damaged settings should prompt for an app again");
        ExpectFailure(() => FileOpener.SaveExecutable(".png", Path.Combine(_folder, "missing.exe"), settingsPath),
            "missing program should not be saved");
        ExpectFailure(() => FileOpener.CreateStartInfo(program, Path.Combine(_folder, "missing.png")),
            "missing file should not be launched");
    }

    private static void FilePrograms()
    {
        foreach (var extension in new[] { ".png", ".jpg", ".json", ".docx" })
        {
            var loading = ShellFileHandlers.ListAsync(extension);
            Check(ReferenceEquals(loading, ShellFileHandlers.ListAsync(extension)), "program lists should be reused per file type");
            var handlers = loading.GetAwaiter().GetResult();
            if (extension is ".png" or ".jpg") Check(handlers.Count > 0, $"Windows should list programs for {extension}");
            Check(handlers.All(handler => !string.IsNullOrWhiteSpace(handler.Name) &&
                                          !string.IsNullOrWhiteSpace(handler.DisplayName)),
                "Open With programs should have identifiers and display names");
            Check(handlers.Select(handler => handler.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == handlers.Count,
                "Open With menu should not repeat a program");
            if (extension == ".png")
            {
                Check(handlers.Any(handler => handler.Icon is not null), "available image programs should include their icons");
                var screenshot = handlers.FirstOrDefault(handler => handler.DisplayName == "BetterScreenShot");
                if (screenshot is not null)
                    Check(screenshot.Icon is not null, "BetterScreenShot should display its own program icon");
            }
        }
        var screenshotIcon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "BetterScreenShot", "assets", "BetterScreenShot.ico");
        if (File.Exists(screenshotIcon))
            Check(ProgramIcons.Extract(screenshotIcon) is not null, "BetterScreenShot's icon asset should be readable");
        Check(ShellFileHandlers.ListAsync("").GetAwaiter().GetResult().Count == 0,
            "files without extensions should still allow a browsed program");
        Check(ShellFileHandlers.Matches(new ShellFileHandlers.Handler(@"C:\Apps\Paint_2\mspaint.exe", "Paint", true),
                @"C:\Apps\Paint_1\mspaint.exe", "Paint"), "updated packaged apps should keep the saved choice");
        Check(!ShellFileHandlers.Matches(new ShellFileHandlers.Handler(@"C:\Apps\Other\mspaint.exe", "Other", true),
                @"C:\Apps\Paint\mspaint.exe", "Paint"), "a different program must not inherit the saved choice");
    }

    private static void OpenWithSmoke(PreviewWindow preview, string settingsPath)
    {
        var paint = ShellFileHandlers.ListAsync(".png").GetAwaiter().GetResult().First(handler =>
            handler.Name.Contains("mspaint.exe", StringComparison.OrdinalIgnoreCase));
        var image = Path.Combine(_folder, "open-with-smoke.png");
        using (var bitmap = new Bitmap(16, 16)) bitmap.Save(image, ImageFormat.Png);
        var original = FileOpener.LoadSelection(".png", settingsPath);
        Check(!PaintIsRunning(), "close Paint before running the optional launch smoke test");
        try
        {
            OpenFromPicker("Open once");
            Check(FileOpener.LoadSelection(".png", settingsPath) == original, "Open once must preserve the saved app");
            ClosePaint();
            OpenFromPicker("Open and remember");
            Check(FileOpener.LoadSelection(".jpg", settingsPath)?.HandlerName == paint.Name,
                "remembered app should apply to the image group");
            ClosePaint();
            preview.ShowFile(image);
            Check(preview.OpenButtonLabel == $"Open in {paint.DisplayName}", "the new preferred app should appear on the button");
            var shortcut = OpenShortcut(preview);
            Check(shortcut.CanExecute(null, preview), "Ctrl+Enter should be available for a file");
            preview.UpdateLayout();
            var label = preview.OpenButtonLabel;
            var buttons = new[] { preview.OpenButton, preview.ChangeProgramButton };
            var before = buttons.Select(FooterAppearance).ToArray();
            shortcut.Execute(null, preview);
            preview.UpdateLayout();
            Check(!shortcut.CanExecute(null, preview), "another launch should be blocked while Paint opens");
            Check(FindVisual<TextBlock>(preview.OpenButton, text => text.IsVisible && text.Text == "Opening…") is not null &&
                  FindVisual<TextBlock>(preview.OpenButton, text => text.IsVisible && text.Text == label) is null,
                "the Open button should show opening feedback while Paint launches");
            Check(preview.OpenButtonLabel == label && buttons.Select(FooterAppearance).SequenceEqual(before),
                "opening feedback must preserve the app label, button positions, sizes, and colors");
            WaitForPickerState(() => !preview.IsVisible && PaintIsRunning());
            preview.ShowFile(image);
            preview.UpdateLayout();
            Check(FindVisual<TextBlock>(preview.OpenButton, text => text.IsVisible && text.Text == label) is not null,
                "reopening the preview should restore the app label");
        }
        finally { ClosePaint(); }

        void OpenFromPicker(string action)
        {
            preview.ShowFile(image);
            preview.ChangeProgramButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            WpfMenuItem? appItem = null;
            WaitForPickerState(() => (appItem = preview.ProgramMenu?.Items.OfType<WpfMenuItem>()
                .FirstOrDefault(item => Equals(item.Header, paint.DisplayName))) is not null);
            appItem!.Items.OfType<WpfMenuItem>().Single(item => Equals(item.Header, action))
                .RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            WaitForPickerState(() => !preview.IsVisible && PaintIsRunning());
        }

        (Rect Bounds, System.Windows.Media.Color Fill, bool Enabled) FooterAppearance(System.Windows.Controls.Button button)
        {
            var chrome = (Border)button.Template.FindName("ButtonChrome", button);
            return (button.TransformToAncestor(preview).TransformBounds(new Rect(button.RenderSize)),
                ((SolidColorBrush)chrome.Background).Color, button.IsEnabled);
        }
    }

    private static bool PaintIsRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("mspaint");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private static void ClosePaint()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("mspaint"))
            using (process)
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit(2000);
            }
    }

    private static RoutedCommand OpenShortcut(PreviewWindow preview) =>
        (RoutedCommand)preview.InputBindings.OfType<KeyBinding>()
            .Single(binding => binding.Key == Key.Enter && binding.Modifiers == ModifierKeys.Control).Command;

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
            Check(view.FormattedText.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
                    .Any(run => run.Text == "\"name\""),
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

        foreach (var encoding in new System.Text.Encoding[] { new System.Text.UTF8Encoding(true),
                     System.Text.Encoding.Unicode, System.Text.Encoding.BigEndianUnicode,
                     System.Text.Encoding.UTF32, new System.Text.UTF32Encoding(true, true) })
        {
            File.WriteAllText(valid, "{\"text\":\"Rīga 😀\"}", encoding);
            using var data = JsonPreviewReader.Read(valid);
            Check(data.Document?.RootElement.GetProperty("text").GetString() == "Rīga 😀", "BOM and wide encodings remain supported");
        }

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
        var settingsPath = Path.Combine(_folder, "window-apps", "settings.json");
        var screenshotHandler = ShellFileHandlers.ListAsync(".png").GetAwaiter().GetResult()
            .FirstOrDefault(handler => handler.DisplayName == "BetterScreenShot");
        FileOpener.SaveHandler(".png", screenshotHandler?.Name ?? "Example.Image", "BetterScreenShot", settingsPath);
        FileOpener.SaveHandler(".json", "Example.Code", "VS Code", settingsPath);
        FileOpener.SaveHandler(".docx", "Example.Word", "Word", settingsPath);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var preview = new PreviewWindow(settingsPath);
        var other = new Window { Width = 100, Height = 100, ShowInTaskbar = false };
        try
        {
            preview.ShowFile(path);
            Check(preview.IsVisible, "preview should open");
            Check(preview.CanOpenFile && preview.OpenButtonLabel == "Open in BetterScreenShot",
                "image preview should name its preferred app on the Open button");
            var pickerIcon = preview.ChangeProgramButton.Content;
            Check(pickerIcon is System.Windows.Shapes.Path { Data: not null },
                "the picker should use the sliders icon when an app is saved");
            var imageChoice = FileOpener.LoadSelection(".png", settingsPath);
            var imageChooser = imageChoice is null ? preview.OpenButton : preview.ChangeProgramButton;
            imageChooser.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            for (var attempt = 0; attempt < 10 && preview.ProgramMenu?.Items.Count <= 2; attempt++)
                PumpDispatcher(TimeSpan.FromMilliseconds(100));
            var programMenu = preview.ProgramMenu;
            Check(programMenu?.IsOpen == true && programMenu.Items.OfType<MenuItem>()
                .Any(item => Equals(item.Header, ShellFileHandlers.ListAsync(".png").GetAwaiter().GetResult()[0].DisplayName)),
                "Change program should show Windows Open With choices");
            Check(programMenu!.Items.OfType<MenuItem>().Any(item => item.Icon is WpfImage icon && icon.Source is not null),
                "program menu should display application icons");
            Check(programMenu.Items.OfType<MenuItem>().Where(item => item.Icon is WpfImage).All(item =>
                    item.Items.OfType<MenuItem>().Select(action => action.Header).SequenceEqual(new object[] { "Open once", "Open and remember" })),
                "each program should offer a one-time action and a remember action");
            var appItem = programMenu.Items.OfType<MenuItem>().First(item => item.Icon is WpfImage);
            programMenu.UpdateLayout();
            var iconStrip = appItem.Template.FindName("IconStrip", appItem) as Border;
            Check(iconStrip?.Background is SolidColorBrush iconBrush && iconBrush.Color == System.Windows.Media.Color.FromRgb(31, 35, 42),
                "the visible app icon strip should use the preview's dark surface color");
            appItem.IsSubmenuOpen = true;
            var openOnce = (MenuItem)appItem.Items[0];
            WaitForPickerState(() => openOnce.IsVisible && openOnce.ActualWidth > 0);
            var submenu = appItem.Template.FindName("PART_Popup", appItem) as Popup;
            Check(submenu?.Child is Border { Background: SolidColorBrush submenuBrush } &&
                  programMenu.Background is SolidColorBrush menuBrush && submenuBrush.Color == menuBrush.Color,
                "the visible submenu should match the program picker background");
            programMenu!.IsOpen = false;
            preview.Width = 420;
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            var change = preview.ChangeProgramButton;
            var rightEdge = change.TransformToAncestor(preview).TransformBounds(new System.Windows.Rect(change.RenderSize)).Right;
            Check(rightEdge <= preview.ActualWidth, "preferred app controls should fit in a narrow preview window");
            preview.Width = 900;
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
            Check(!preview.CanOpenFile, "folder preview should not show file program controls");
            Check(!OpenShortcut(preview).CanExecute(null, preview), "Ctrl+Enter should not launch a program for a folder");
            Check(ContainsText(preview, "LOCATION") && ContainsText(preview, folder.FullName),
                "folder preview should show a labeled location");
            Check(ContainsText(preview, "SIZE") && ContainsText(preview, "CONTENTS"),
                "folder preview should show size and item count fields");
            var json = Path.Combine(_folder, "window.json");
            File.WriteAllText(json, "{\"items\":[" + string.Join(',', Enumerable.Range(0, 300).Select(i => $"{{\"number\":{i}}}")) + "]}");
            preview.Hide();
            preview.ShowFile(json);
            Check(preview.CanOpenFile && preview.OpenButtonLabel == "Open in VS Code",
                "JSON preview should name its preferred app on the Open button");
            var jsonChoice = FileOpener.LoadSelection(".json", settingsPath);
            var jsonChooser = jsonChoice is null ? preview.OpenButton : preview.ChangeProgramButton;
            jsonChooser.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            for (var attempt = 0; attempt < 10 && preview.ProgramMenu?.Items.Count <= 2; attempt++)
                PumpDispatcher(TimeSpan.FromMilliseconds(100));
            var jsonHandlers = ShellFileHandlers.ListAsync(".json").GetAwaiter().GetResult();
            Check(preview.ProgramMenu?.IsOpen == true &&
                  (jsonHandlers.Count == 0 || preview.ProgramMenu.Items.OfType<MenuItem>()
                      .Any(item => Equals(item.Header, jsonHandlers[0].DisplayName))),
                "JSON menu should show available programs");
            preview.ProgramMenu!.IsOpen = false;
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
            var docx = Path.Combine(_folder, "window.docx");
            CreateZip(docx, ("word/document.xml",
                "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body/></w:document>"));
            preview.Hide();
            preview.ShowFile(docx);
            Check(preview.CanOpenFile && preview.OpenButtonLabel == "Open in Word",
                "Word preview should offer a separately chosen program");
            var unknown = Path.Combine(_folder, "window.unknown");
            File.WriteAllText(unknown, "sample");
            preview.Hide();
            preview.ShowFile(unknown);
            Check(preview.CanOpenFile && preview.OpenButtonLabel == "Choose app",
                "unconfigured file types should offer a program choice");
            Check(ReferenceEquals(preview.ChangeProgramButton.Content, pickerIcon),
                "the picker should keep the same sliders icon when no app is saved");
            OpenShortcut(preview).Execute(null, preview);
            Check(preview.ProgramMenu?.IsOpen == true, "Ctrl+Enter should open the picker when no program is saved");
            preview.ProgramMenu!.IsOpen = false;
            Run("Rapid preview switching and stale Office callbacks", () => PreviewReplacement(preview, path, json, docx, unknown));
            if (Environment.GetCommandLineArgs().Contains("--open-with-smoke"))
                Run("Open once, remember, and Ctrl+Enter launch Paint", () => OpenWithSmoke(preview, settingsPath));
        }
        finally
        {
            other.Close();
            preview.ClosePermanently();
            app.Shutdown();
        }
    }

    private static void PreviewReplacement(PreviewWindow preview, string image, string json, string document, string unknown)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = PreviewWork.RunAsync(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return 0;
        }, CancellationToken.None);
        try
        {
            Check(started.Wait(TimeSpan.FromSeconds(5)), "blocked decoder starts");
            preview.ShowFile(json);
            preview.ShowFile(image);
            preview.ShowFile(document);
            preview.ShowFile(unknown);
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            Check(ContainsText(preview, unknown), "stale deferred Office callback cannot replace the current preview");
        }
        finally { release.Set(); }
        active.GetAwaiter().GetResult();
        PumpDispatcher(TimeSpan.FromMilliseconds(100));
        Check(ContainsText(preview, unknown), "canceled image and JSON work cannot replace the current preview");
        preview.ShowFile(image);
        for (var i = 0; i < 20 && FindVisual<WpfImage>(preview, item => item.Source is not null) is null; i++)
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
        Check(FindVisual<WpfImage>(preview, item => item.Source is not null) is not null, "image loading recovers after cancellation");
        preview.ShowFile(json);
        preview.Hide();
        PumpDispatcher(TimeSpan.FromMilliseconds(100));
        Check(!preview.IsVisible, "completion never reopens a closed preview");
        preview.ShowFile(json);
        for (var i = 0; i < 20 && !ContainsText(preview, json); i++) PumpDispatcher(TimeSpan.FromMilliseconds(100));
        Check(ContainsText(preview, json), "JSON can reopen after cancellation");
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
