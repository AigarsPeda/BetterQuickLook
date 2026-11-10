using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using BetterQuickLook;

internal static class EdgeCases
{
    internal static void Folders(string root)
    {
        var folder = Path.Combine(root, "folder-edges");
        Directory.CreateDirectory(folder);
        var empty = FolderPreview.Scan(folder, CancellationToken.None, 1);
        Check(empty.Bytes == 0 && empty.Files == 0 && !empty.IsPartial, "empty folder");
        File.WriteAllBytes(Path.Combine(folder, "hidden.txt"), new byte[17]);
        File.SetAttributes(Path.Combine(folder, "hidden.txt"), FileAttributes.Hidden);
        File.WriteAllBytes(Path.Combine(folder, "zero.txt"), []);
        var exact = FolderPreview.Scan(folder, CancellationToken.None, 2);
        Check(exact.Bytes == 17 && exact.Files == 2 && !exact.ReachedLimit, "exact entry limit and hidden files");
        var limited = FolderPreview.Scan(folder, CancellationToken.None, 1);
        Check(limited.Files == 1 && limited.ReachedLimit, "limit below entry count");
        try { FolderPreview.Scan(folder, CancellationToken.None, 0); throw new Exception("zero limit accepted"); }
        catch (ArgumentOutOfRangeException) { }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { FolderPreview.Scan(folder, cancellation.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { }
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllBytes(Path.Combine(folder, "nested", "Rīga.txt"), new byte[23]);
        var nested = FolderPreview.Scan(folder, CancellationToken.None);
        Check(nested.Bytes == 40 && nested.Files == 3 && nested.Folders == 1, "nested size and Unicode names");
    }

    internal static void JsonFormatting(string root)
    {
        var file = Path.Combine(root, "formatting.json");
        var inputs = new[] { "null", "42", "{}", "[]", "{\"value\":\"Rīga 漢字 😀\",\"n\":1e-8}",
            JsonSerializer.Serialize(Enumerable.Range(0, 20000).Select(i => new { i, value = "Rīga 漢字" })),
            JsonSerializer.Serialize(new string('x', 600000)) };
        foreach (var input in inputs)
        {
            File.WriteAllText(file, input);
            using var expectedDocument = JsonDocument.Parse(input);
            var expected = JsonSerializer.Serialize(expectedDocument.RootElement, new JsonSerializerOptions
                { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            using var actual = JsonPreviewReader.Read(file);
            Check(actual.Document is not null, "tree retained for supported files");
            Check(actual.Text == (expected.Length > JsonPreviewReader.MaxShownChars
                ? expected[..JsonPreviewReader.MaxShownChars] + "\n…" : expected), "formatted prefix matches reference serializer");
        }
        File.WriteAllText(file, new string('[', 70) + "0" + new string(']', 70));
        using var deep = JsonPreviewReader.Read(file);
        Check(deep.Document is null && deep.Notice!.StartsWith("Invalid"), "excessive nesting is handled");
    }

    internal static void UnicodeSample(string root)
    {
        var file = Path.Combine(root, "unicode-large.json");
        File.WriteAllText(file, new string('x', JsonPreviewReader.MaxShownChars - 1) + "😀" +
            new string('x', JsonPreviewReader.MaxFileBytes));
        using var data = JsonPreviewReader.Read(file);
        Check(!char.IsHighSurrogate(data.Text[^1]), "large-file sample must not split a Unicode character");
    }

    internal static void ImageLifetime(string root)
    {
        var file = Path.Combine(root, "portrait Rīga.png");
        using (var bitmap = new Bitmap(1000, 4000)) bitmap.Save(file, ImageFormat.Png);
        var image = PreviewWindow.DecodeImage(file);
        Check(image.PixelHeight == 1800 && image.PixelWidth == 450 && image.IsFrozen, "portrait dimensions and thread-safe image");
        using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check(exclusive.Length > 0, "decoded preview releases the file handle");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
