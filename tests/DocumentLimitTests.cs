using System.IO;
using BetterQuickLook;

internal static partial class Program
{
    private static void DocumentTextLimits()
    {
        var huge = new string('x', 39_999) + "😀" + new string('x', 1_000_000);
        var word = Path.Combine(_folder, "huge.docx");
        CreateZip(word, ("word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:p><w:r><w:t>" +
            huge + "</w:t></w:r></w:p></w:document>"));
        Check(DocumentPreview.Read(word) == new string('x', 39_999), "Word stops inside a long run without splitting a Unicode character");
        foreach (var shared in new[] { false, true })
        {
            var workbook = Path.Combine(_folder, $"huge-{shared}.xlsx");
            CreateZip(workbook,
                ("xl/sharedStrings.xml", "<sst><si><t>" + huge + "</t></si></sst>"),
                ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row>" +
                 (shared ? "<c r='A1' t='s'><v>0</v></c>" : "<c r='A1' t='inlineStr'><is><t>" + huge + "</t></is></c>") +
                 "<c r='B1'><v>999</v></c></row></sheetData></worksheet>"));
            Check(DocumentPreview.Read(workbook) == new string('x', 39_999), "Excel shared/inline long cells obey the output cap");
        }
        var cdata = Path.Combine(_folder, "cdata.docx");
        CreateZip(cdata, ("word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:p><w:r>" +
            "<w:t/><w:t>A&amp;B<![CDATA[<C>]]></w:t><w:t>D</w:t></w:r></w:p></w:document>"));
        Check(DocumentPreview.Read(cdata).TrimEnd() == "A&B<C>D", "empty nodes, entities, CDATA and consecutive text are preserved");
    }
}
