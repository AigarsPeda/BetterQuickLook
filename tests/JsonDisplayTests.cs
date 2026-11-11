using System.Windows.Documents;
using System.Windows.Media;
using BetterQuickLook;

internal static partial class Program
{
    private static void JsonDisplay()
    {
        foreach (var text in new[] { "", "{}", "{\n  \"name\": \"Rīga 😀\",\n  \"n\": -12.5,\n  \"yes\": true\n}",
                     "first\r\n\r\nlast\r\n", "\"unfinished\nstring", new string('x', 100_000),
                     "[\n" + string.Join(",\n", Enumerable.Repeat("1", 6000)) + "\n]" })
        {
            using var data = new JsonPreviewData(text, null, null);
            using var view = new JsonPreviewView("sample.json", data);
            var paragraphs = view.FormattedText.Document.Blocks.OfType<Paragraph>().ToArray();
            var actual = string.Join("\n", paragraphs.Select(p => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text))));
            Check(actual.Replace("\r\n", "\n") == text.Replace("\r\n", "\n"), "layout preserves all text, including uncolored tail and empty lines");
            Check(!view.FormattedText.IsUndoEnabled, "read-only preview does not retain undo history");
            foreach (var run in paragraphs.SelectMany(p => p.Inlines.OfType<Run>()))
            {
                if (run.Text == "\"name\"")
                    Check(run.Foreground is SolidColorBrush brush && brush.IsFrozen && brush.Color == System.Windows.Media.Color.FromRgb(139, 213, 255),
                        "property name retains its theme color");
                if (run.Text == "-12.5")
                    Check(((SolidColorBrush)run.Foreground).Color == System.Windows.Media.Color.FromRgb(250, 179, 135), "number color");
            }
        }
    }
}
