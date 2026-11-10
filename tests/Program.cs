using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Windows;
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
            Run("Word text and empty document", Word);
            Run("Excel shared, inline, and numeric cells", Excel);
            Run("Missing and malformed document parts", InvalidDocuments);
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

    private static void WindowLifecycle()
    {
        var path = Path.Combine(_folder, "visible.png");
        using (var image = new Bitmap(20, 20)) image.Save(path, ImageFormat.Png);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var preview = new PreviewWindow();
        var other = new Window { Width = 100, Height = 100, ShowInTaskbar = false };
        try
        {
            preview.ShowFile(path);
            Check(preview.IsVisible, "preview should open");
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
        }
        finally
        {
            other.Close();
            preview.ClosePermanently();
            app.Shutdown();
        }
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
