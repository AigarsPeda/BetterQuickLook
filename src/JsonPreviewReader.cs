using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BetterQuickLook;

internal sealed class JsonPreviewData(string text, string? notice, JsonDocument? document) : IDisposable
{
    public string Text { get; } = text;
    public string? Notice { get; } = notice;
    public JsonDocument? Document { get; } = document;

    public void Dispose() => Document?.Dispose();
}

internal static class JsonPreviewReader
{
    internal const int MaxFileBytes = 2 * 1024 * 1024;
    internal const int MaxShownChars = 100_000;

    internal static JsonPreviewData Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            using var reader = new StreamReader(path);
            var buffer = new char[MaxShownChars];
            var count = reader.ReadBlock(buffer);
            return new JsonPreviewData(new string(buffer, 0, count),
                "Large file: showing the first 100,000 characters. Tree view is unavailable.", null);
        }

        var raw = File.ReadAllText(path);
        var options = Path.GetExtension(path).Equals(".jsonc", StringComparison.OrdinalIgnoreCase)
            ? new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
            : default;
        try
        {
            var document = JsonDocument.Parse(raw, options);
            var formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var truncated = formatted.Length > MaxShownChars;
            return new JsonPreviewData(Clip(formatted),
                truncated ? "Formatted text is shortened to 100,000 characters. The tree remains available." : null,
                document);
        }
        catch (JsonException error)
        {
            var line = (error.LineNumber ?? 0) + 1;
            var position = (error.BytePositionInLine ?? 0) + 1;
            return new JsonPreviewData(Clip(raw),
                $"Invalid JSON at line {line}, byte {position}: {error.Message}", null);
        }
    }

    private static string Clip(string text)
    {
        if (text.Length <= MaxShownChars) return text;
        var end = MaxShownChars;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end] + "\n…";
    }
}
