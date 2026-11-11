using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterQuickLook;
using Button = System.Windows.Controls.Button;

internal static partial class Program
{
    private static void FolderContentsTests()
    {
        var root = Directory.CreateDirectory(Path.Combine(_folder, "contents")).FullName;
        Check(FolderContents.Read(root, CancellationToken.None).Items.Length == 0, "empty folder");
        var child = Directory.CreateDirectory(Path.Combine(root, "Subfolder")).FullName;
        File.WriteAllText(Path.Combine(child, "nested.txt"), "not a direct child");
        var file = Path.Combine(root, "Rīga 😀.json"); File.WriteAllText(file, "{}");
        File.SetAttributes(file, FileAttributes.Hidden);
        var result = FolderContents.Read(root, CancellationToken.None);
        Check(result.Items.Length == 2 && !result.HasMore && result.Items[0].IsFolder, "direct children only, folders first");
        Check(result.Items[1].Path == file && result.Items[1].Bytes == 2 && result.Items[1].Modified.Year == DateTime.Now.Year,
            "hidden Unicode names and metadata");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FolderContents.Read(root, cancelled.Token); throw new Exception("cancelled read continued"); }
        catch (OperationCanceledException) { }
        try { FolderContents.Read(Path.Combine(root, "missing"), CancellationToken.None); throw new Exception("missing directory accepted"); }
        catch (DirectoryNotFoundException) { }
        for (var i = 0; i < FolderContents.Limit - 2; i++) File.WriteAllText(Path.Combine(root, $"item-{i}.txt"), "");
        Check(!FolderContents.Read(root, CancellationToken.None).HasMore, "exact limit has no omitted items");
        for (var i = FolderContents.Limit; i < 2000; i++) File.WriteAllText(Path.Combine(root, $"item-{i}.txt"), "");
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            result = FolderContents.Read(root, CancellationToken.None);
            Check(result.Items.Length == FolderContents.Limit && result.HasMore, "large directory stays bounded");
        }
        Console.WriteLine($"Folder list: {timer.Elapsed.TotalMilliseconds / 20:F2} ms average, 20 reads of a 2,000-item directory.");
    }

    private static void FolderNavigationTests(PreviewWindow preview)
    {
        var root = Directory.CreateDirectory(Path.Combine(_folder, "browse")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(root, "Documents")).FullName;
        var file = Path.Combine(root, "Sample.json"); File.WriteAllText(file, "{\"preview\":true}");
        var nested = Path.Combine(child, "Nested.json"); File.WriteAllText(nested, "{}");
        Button Row(string path)
        {
            Until(() => FindVisual<Button>(preview, button => Equals(button.Tag, path)) is { IsVisible: true }, "visible folder row " + Path.GetFileName(path));
            return FindVisual<Button>(preview, button => Equals(button.Tag, path))!;
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        preview.ShowFile(root);
        Row(file);
        Check(preview.BackButton.Visibility == Visibility.Collapsed, "initial folder has no back history");
        Check(FindVisual<Button>(preview, button => Equals(button.Content, "Open folder")) is { IsVisible: true } &&
              FindVisual<Button>(preview, button => Equals(button.Content, "Copy path")) is { IsVisible: true }, "visible folder quick actions");
        preview.Width = 420; preview.UpdateLayout();
        var narrowRow = Row(file);
        Check(narrowRow.ActualWidth > 100 && narrowRow.TransformToAncestor(preview).TransformBounds(new Rect(narrowRow.RenderSize)).Right <= preview.ActualWidth,
            "folder row fits narrow windows");
        preview.Width = 900;
        Click(Row(file));
        Until(() => FindVisual<JsonPreviewView>(preview, _ => true) is not null, "click file shows JSON");
        Check(preview.BackButton.IsVisible, "child preview has Back");
        Click(preview.BackButton); Row(file);
        Click(Row(child)); Row(nested);
        Click(Row(nested));
        Until(() => FindVisual<JsonPreviewView>(preview, _ => true) is not null, "nested JSON");
        Click(preview.BackButton); Row(nested);
        Click(preview.BackButton); Row(file);
        Check(!preview.BackButton.IsVisible, "Back unwinds nested navigation");
        var doomed = Row(file); File.Delete(file); Click(doomed);
        Check(ContainsText(preview, "File unavailable") && preview.BackButton.IsVisible, "deleted child remains recoverable with Back");
        Click(preview.BackButton); Row(child);
        Click(Row(child)); Row(nested);
        preview.ShowFile(nested, activate: false);
        Check(!preview.BackButton.IsVisible, "new Explorer selection clears internal history");
        preview.ShowFile(root); Click(Row(child)); Row(nested); preview.Hide();
        preview.ShowFile(root); Row(child);
        Check(!preview.BackButton.IsVisible, "close and reopen clears history");
        preview.ShowFile(CreateShortcut("browse-folder", root)); Row(child); Click(Row(child)); Row(nested);
        Click(preview.BackButton); Row(child);
        Check(ContainsText(preview, "browse-folder.lnk"), "Back preserves shortcut entry point");
        preview.ShowFile(root); preview.ShowFile(nested);
        Until(() => FindVisual<JsonPreviewView>(preview, _ => true) is not null, "rapid selection shows newest file");
        Check(FindVisual<Button>(preview, button => button.Tag is string) is null, "stale listing cannot replace newer preview");
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--render-folder"))
        {
            File.WriteAllText(file, "{}");
            preview.ShowFile(root); Row(file); preview.UpdateLayout();
            var image = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(preview);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(args[^1]); encoder.Save(output);
        }
    }
}
