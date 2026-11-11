using System.IO;
using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace BetterQuickLook;

internal static class DocumentPreview
{
    private const int MaxOutput = 40_000;
    private const long MaxEntryBytes = 30_000_000;

    public static string Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)
            ? ReadWord(archive)
            : ReadExcel(archive);
    }

    private static string ReadWord(ZipArchive archive)
    {
        using var reader = OpenXml(archive, "word/document.xml");
        var result = new StringBuilder();
        while (!reader.EOF && result.Length < MaxOutput)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && reader.NamespaceURI.EndsWith("wordprocessingml/2006/main"))
            {
                AppendElementText(reader, result);
                continue;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                result.AppendLine();
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "tc")
                result.Append("  |  ");
            reader.Read();
        }
        return result.Length == 0 ? "This document contains no readable text." : OutputPrefix(result);
    }

    private static string ReadExcel(ZipArchive archive)
    {
        // Read the displayed cells first. Most of a workbook's shared text table
        // belongs to rows or sheets that this preview will never show.
        using var sheet = OpenXml(archive, "xl/worksheets/sheet1.xml");
        var rows = new List<SortedDictionary<int, ExcelCell>>();
        var cells = new SortedDictionary<int, ExcelCell>();
        var needed = new HashSet<int>();
        while (rows.Count < 100 && sheet.Read())
        {
            if (sheet.NodeType == XmlNodeType.Element && sheet.LocalName == "c")
            {
                var reference = sheet.GetAttribute("r") ?? "";
                var column = ColumnIndex(reference);
                var type = sheet.GetAttribute("t");
                if (column < 0 || column >= 16) continue;
                using var cell = sheet.ReadSubtree();
                var value = ReadCellText(cell);
                if (value.Length == 0) continue;
                int? sharedIndex = type == "s" && int.TryParse(value, out var index) && index >= 0 && index < 100_000
                    ? index : null;
                if (sharedIndex is { } required) needed.Add(required);
                cells[column] = new ExcelCell(value, sharedIndex);
            }
            else if (sheet.LocalName == "row" && (sheet.NodeType == XmlNodeType.EndElement || sheet.IsEmptyElement))
            {
                rows.Add(cells);
                cells = new SortedDictionary<int, ExcelCell>();
            }
        }

        var strings = new Dictionary<int, string>();
        if (needed.Count > 0 && archive.GetEntry("xl/sharedStrings.xml") is not null)
        {
            using var reader = OpenXml(archive, "xl/sharedStrings.xml");
            var index = 0;
            while (needed.Count > 0 && !reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
                {
                    if (needed.Remove(index))
                    {
                        using var text = reader.ReadSubtree();
                        strings[index] = ReadCellText(text);
                    }
                    else { reader.Skip(); index++; continue; }
                    index++;
                }
                reader.Read();
            }
        }

        var result = new StringBuilder();
        foreach (var row in rows)
        {
            if (result.Length >= MaxOutput) break;
            if (row.Count == 0) continue;
            var lastColumn = row.Keys.Max();
            for (var column = 0; column <= lastColumn && result.Length < MaxOutput; column++)
            {
                if (column > 0) result.Append('\t');
                if (!row.TryGetValue(column, out var cell)) continue;
                var value = cell.SharedIndex is { } index ? strings.GetValueOrDefault(index, cell.Value) : cell.Value;
                value = value.Replace('\r', ' ').Replace('\n', ' ');
                result.Append(value.AsSpan(0, Math.Min(value.Length, MaxOutput - result.Length)));
            }
            result.AppendLine();
        }
        return result.Length == 0 ? "This worksheet contains no readable cells." : OutputPrefix(result);
    }

    private sealed record ExcelCell(string Value, int? SharedIndex);

    private static string ReadCellText(XmlReader reader)
    {
        var value = new StringBuilder();
        while (!reader.EOF && value.Length < MaxOutput)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "v" or "t")
            {
                AppendElementText(reader, value);
                continue;
            }
            reader.Read();
        }
        return value.ToString();
    }

    private static string OutputPrefix(StringBuilder text)
    {
        var length = Math.Min(text.Length, MaxOutput);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text.ToString(0, length);
    }

    private static void AppendElementText(XmlReader reader, StringBuilder text)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        var depth = reader.Depth;
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            while (text.Length < MaxOutput && reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    reader.Read();
                    return;
                }
                if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)) continue;
                while (text.Length < MaxOutput)
                {
                    // XmlReader requires space for both halves of a surrogate pair.
                    var remaining = MaxOutput - text.Length;
                    var count = reader.ReadValueChunk(buffer, 0, Math.Min(buffer.Length, Math.Max(2, remaining)));
                    if (count == 0) break;
                    text.Append(buffer, 0, Math.Min(count, remaining));
                }
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }

    private static int ColumnIndex(string reference)
    {
        var result = 0;
        foreach (var c in reference)
        {
            if (c is < 'A' or > 'Z') break;
            result = result * 26 + c - 'A' + 1;
        }
        return result - 1;
    }

    private static XmlReader OpenXml(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing {name}");
        if (entry.Length > MaxEntryBytes) throw new InvalidDataException("Document part is too large to preview.");
        return XmlReader.Create(entry.Open(), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = true
        });
    }
}
