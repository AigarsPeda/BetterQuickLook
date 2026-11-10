using System.IO;
using System.Text;
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
            if (count > 0 && char.IsHighSurrogate(buffer[count - 1])) count--;
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
            string formatted;
            try { formatted = FormatPrefix(document.RootElement); }
            catch { document.Dispose(); throw; }
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

    private static string FormatPrefix(JsonElement value)
    {
        // Four UTF-8 bytes per displayed UTF-16 character leaves enough room for
        // every Unicode prefix. Stop serializing once the display budget is full.
        using var output = new PrefixStream(MaxShownChars * 4);
        try
        {
            using var writer = new Utf8JsonWriter(output, new JsonWriterOptions
            { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            WriteValue(value, writer);
        }
        catch (PreviewLimitException) { }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static void WriteValue(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteValue(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var child in value.EnumerateArray()) WriteValue(child, writer);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
        // Utf8JsonWriter buffers until Flush; bound that buffer as we traverse.
        if (writer.BytesPending >= 8192) writer.Flush();
    }

    private sealed class PreviewLimitException : Exception { }

    private sealed class PrefixStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            var remaining = Math.Min(count, limit - (int)Length);
            base.Write(buffer, offset, remaining);
            if (remaining < count) throw new PreviewLimitException();
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
