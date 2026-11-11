using System.IO;
using BetterQuickLook;

internal static partial class Program
{
    private static void ExcelEdges()
    {
        foreach (var index in new[] { -1, 15, 99_999 })
        {
            var path = ExcelFixtures.LargeWorkbook(_folder, index);
            var expected = index < 0 ? "42" : $"Shared value {index}: Rīga sample text";
            Check(DocumentPreview.Read(path) == expected + Environment.NewLine, "large shared table resolves exact value");
            File.Delete(path); // All ZIP/XML handles must be released after reading.
        }

        var sparse = Path.Combine(_folder, "sparse.xlsx");
        CreateZip(sparse,
            ("xl/sharedStrings.xml", "<sst><si/><si><r><t>Rīga </t></r><r><t>😀</t></r></si><si><t>Last</t></si></sst>"),
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row>" +
             "<c r='D1' t='s'><v>1</v></c><c r='A1' t='s'><v>2</v></c><c r='B1' t='s'><v>0</v></c>" +
             "<c r='C1' t='s'><v>1</v></c><c r='E1' t='inlineStr'><is><r><t>One</t></r><r><t>Two</t></r></is></c>" +
             "<c r='G1' t='s'><v>999</v></c><c r='H1' t='s'><v>-1</v></c>" +
             "</row></sheetData></worksheet>"));
        Check(DocumentPreview.Read(sparse) == "Last\t\tRīga 😀\tRīga 😀\tOneTwo\t\t999\t-1" + Environment.NewLine,
            "empty entries, rich text, repeated and out-of-order references, gaps, and invalid indices");

        var limited = Path.Combine(_folder, "limited.xlsx");
        CreateZip(limited,
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData>" + string.Concat(Enumerable.Repeat("<row/>", 99)) +
             "<row><c r='P100'><v>16</v></c><c r='Q100' t='s'><v>0</v></c></row>" +
             "<row><c r='A101' t='s'><v>0</v></c></row></sheetData></worksheet>"),
            ("xl/sharedStrings.xml", "not XML: this unused table must not be opened"));
        Check(DocumentPreview.Read(limited) == new string('\t', 15) + "16" + Environment.NewLine,
            "100-row and 16-column limits, including empty rows; no unused shared table read");

        var missing = Path.Combine(_folder, "missing-strings.xlsx");
        CreateZip(missing, ("xl/worksheets/sheet1.xml",
            "<worksheet><sheetData><row><c r='A1' t='s'><v>5</v></c></row></sheetData></worksheet>"));
        Check(DocumentPreview.Read(missing) == "5" + Environment.NewLine, "missing shared table retains raw value");
        var bad = Path.Combine(_folder, "bad-strings.xlsx");
        CreateZip(bad, ("xl/worksheets/sheet1.xml",
            "<worksheet><sheetData><row><c r='A1' t='s'><v>0</v></c></row></sheetData></worksheet>"),
            ("xl/sharedStrings.xml", "<!DOCTYPE sst [<!ENTITY unsafe SYSTEM 'file:///missing'>]><sst><si><t>&unsafe;</t></si></sst>"));
        ExpectFailure(() => DocumentPreview.Read(bad), "DTD in a required shared table must be rejected");
    }
}
