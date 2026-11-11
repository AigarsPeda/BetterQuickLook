using System.IO;
using System.IO.Compression;
using System.Text;

internal static class ExcelFixtures
{
    internal static string LargeWorkbook(string root, int index)
    {
        var path = Path.Combine(root, $"shared-{index}.xlsx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("xl/sharedStrings.xml").Open(), Encoding.UTF8))
        {
            writer.Write("<sst>");
            for (var i = 0; i < 100_000; i++) writer.Write($"<si><t>Shared value {i}: Rīga sample text</t></si>");
            writer.Write("</sst>");
        }
        using (var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open(), Encoding.UTF8))
        {
            writer.Write("<worksheet><sheetData><row>");
            writer.Write(index < 0 ? "<c r='A1'><v>42</v></c>" : $"<c r='A1' t='s'><v>{index}</v></c>");
            writer.Write("</row></sheetData></worksheet>");
        }
        return path;
    }
}
