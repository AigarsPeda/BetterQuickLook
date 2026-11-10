using System.IO;
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
                result.Append(reader.ReadElementContentAsString());
                continue;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                result.AppendLine();
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "tc")
                result.Append("  |  ");
            reader.Read();
        }
        return result.Length == 0 ? "This document contains no readable text." : result.ToString();
    }

    private static string ReadExcel(ZipArchive archive)
    {
        var strings = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is not null)
        {
            using var reader = OpenXml(archive, "xl/sharedStrings.xml");
            var current = new StringBuilder();
            while (!reader.EOF && strings.Count < 100_000)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    current.Append(reader.ReadElementContentAsString());
                    continue;
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si")
                {
                    strings.Add(current.ToString());
                    current.Clear();
                }
                reader.Read();
            }
        }

        using var sheet = OpenXml(archive, "xl/worksheets/sheet1.xml");
        var result = new StringBuilder();
        var cells = new SortedDictionary<int, string>();
        var rows = 0;
        while (sheet.Read() && rows < 100 && result.Length < MaxOutput)
        {
            if (sheet.NodeType == XmlNodeType.Element && sheet.LocalName == "c")
            {
                var reference = sheet.GetAttribute("r") ?? "";
                var column = ColumnIndex(reference);
                var type = sheet.GetAttribute("t");
                if (column < 0 || column >= 16) continue;
                using var cell = sheet.ReadSubtree();
                string? value = null;
                while (cell.Read())
                    if (cell.NodeType == XmlNodeType.Element && cell.LocalName is "v" or "t")
                        value = cell.ReadElementContentAsString();
                if (value is null) continue;
                if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < strings.Count)
                    value = strings[index];
                cells[column] = value.Replace('\r', ' ').Replace('\n', ' ');
            }
            else if (sheet.NodeType == XmlNodeType.EndElement && sheet.LocalName == "row")
            {
                if (cells.Count > 0)
                {
                    result.AppendLine(string.Join("\t", Enumerable.Range(0, cells.Keys.Max() + 1)
                        .Select(i => cells.GetValueOrDefault(i, ""))));
                    cells.Clear();
                }
                rows++;
            }
        }
        return result.Length == 0 ? "This worksheet contains no readable cells." : result.ToString();
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
