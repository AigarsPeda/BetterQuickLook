using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using WpfButton = System.Windows.Controls.Button;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTreeView = System.Windows.Controls.TreeView;
using WpfTreeViewItem = System.Windows.Controls.TreeViewItem;

namespace BetterQuickLook;

internal sealed class JsonPreviewView : Grid, IDisposable
{
    private const int MaxTreeChildren = 500;
    private const int MaxColoredTokens = 5_000;
    private static readonly Brush PropertyBrush = FrozenBrush(139, 213, 255);
    private static readonly Brush StringBrush = FrozenBrush(166, 227, 161);
    private static readonly Brush NumberBrush = FrozenBrush(250, 179, 135);
    private static readonly Brush LiteralBrush = FrozenBrush(203, 166, 247);
    private static readonly Regex Tokens = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|-?(?:0|[1-9]\\d*)(?:\\.\\d+)?(?:[eE][+-]?\\d+)?|\\b(?:true|false|null)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly JsonPreviewData _data;
    internal WpfTreeView Tree { get; }
    internal WpfRichTextBox FormattedText { get; }

    public JsonPreviewView(string path, JsonPreviewData data)
    {
        _data = data;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var location = new WpfTextBox
        {
            Text = path,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 174, 187)),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Margin = new Thickness(18, 13, 18, 8)
        };
        Children.Add(location);

        if (data.Notice is { } notice)
        {
            var message = new TextBlock
            {
                Text = notice,
                TextWrapping = TextWrapping.Wrap,
                Foreground = notice.StartsWith("Invalid", StringComparison.Ordinal)
                    ? new SolidColorBrush(Color.FromRgb(255, 150, 150))
                    : new SolidColorBrush(Color.FromRgb(240, 191, 116)),
                FontSize = 12,
                Margin = new Thickness(18, 0, 18, 10)
            };
            SetRow(message, 1);
            Children.Add(message);
        }

        FormattedText = new WpfRichTextBox
        {
            IsReadOnly = true,
            IsUndoEnabled = false,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        BuildDocument(FormattedText, data.Text);
        Tree = new WpfTreeView
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(10)
        };
        if (data.Document is { } document)
            Tree.Items.Add(MakeTreeItem("$", document.RootElement));

        var formattedButton = MakeButton("Formatted", true);
        var treeButton = MakeButton("Tree", false);
        treeButton.IsEnabled = data.Document is not null;
        formattedButton.Click += (_, _) =>
        {
            FormattedText.Visibility = Visibility.Visible;
            Tree.Visibility = Visibility.Collapsed;
            SetSelected(formattedButton, treeButton);
        };
        treeButton.Click += (_, _) =>
        {
            FormattedText.Visibility = Visibility.Collapsed;
            Tree.Visibility = Visibility.Visible;
            SetSelected(treeButton, formattedButton);
        };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(18, 0, 18, 8)
        };
        toolbar.Children.Add(formattedButton);
        toolbar.Children.Add(treeButton);
        SetRow(toolbar, 2);
        Children.Add(toolbar);

        SetRow(FormattedText, 3);
        SetRow(Tree, 3);
        Children.Add(FormattedText);
        Children.Add(Tree);
    }

    private static WpfButton MakeButton(string label, bool selected) => new()
    {
        Content = label,
        Background = new SolidColorBrush(selected ? Color.FromRgb(64, 85, 116) : Color.FromRgb(43, 48, 56)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(15, 6, 15, 6),
        Margin = new Thickness(0, 0, 7, 0),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static void SetSelected(WpfButton selected, WpfButton other)
    {
        selected.Background = new SolidColorBrush(Color.FromRgb(64, 85, 116));
        other.Background = new SolidColorBrush(Color.FromRgb(43, 48, 56));
    }

    private static void BuildDocument(WpfRichTextBox box, string text)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(224, 231, 239)),
            PagePadding = new Thickness(16)
        };
        box.Document = document;
        // Insert into one text container and publish one change, rather than
        // allocating and merging a separate text container for every token.
        using (box.DeclareChangeBlock())
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            document.Blocks.Add(paragraph);
            var position = 0;
            var colored = 0;
            foreach (Match match in Tokens.Matches(text))
            {
                if (++colored > MaxColoredTokens) break;
                if (match.Index > position) _ = new Run(text[position..match.Index], paragraph.ContentEnd);
                var token = match.Value;
                var brush = token[0] == '"'
                    ? IsProperty(text, match.Index + match.Length) ? PropertyBrush : StringBrush
                    : token[0] == '-' || char.IsDigit(token[0]) ? NumberBrush : LiteralBrush;
                _ = new Run(token, paragraph.ContentEnd) { Foreground = brush };
                position = match.Index + match.Length;
            }
            if (position < text.Length) _ = new Run(text[position..], paragraph.ContentEnd);
        }
    }

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static bool IsProperty(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        return position < text.Length && text[position] == ':';
    }

    private static WpfTreeViewItem MakeTreeItem(string name, JsonElement value)
    {
        var item = new WpfTreeViewItem
        {
            Header = $"{name}: {Describe(value)}",
            Foreground = Brushes.White
        };
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return item;

        item.Items.Add(new WpfTreeViewItem { Header = "…" });
        item.Expanded += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, item) || item.Tag is true) return;
            item.Tag = true;
            item.Items.Clear();
            var count = 0;
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    if (count++ == MaxTreeChildren) break;
                    item.Items.Add(MakeTreeItem(property.Name, property.Value));
                }
            }
            else
            {
                foreach (var child in value.EnumerateArray())
                {
                    if (count == MaxTreeChildren) break;
                    item.Items.Add(MakeTreeItem($"[{count++}]", child));
                }
            }
            if (count > MaxTreeChildren || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > MaxTreeChildren)
                item.Items.Add(new WpfTreeViewItem { Header = "Only the first 500 items are shown.", Foreground = Brushes.Gray });
        };
        return item;
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{…}",
        JsonValueKind.Array => "[…]",
        JsonValueKind.String => Shorten(JsonSerializer.Serialize(value.GetString())),
        _ => Shorten(value.GetRawText())
    };

    private static string Shorten(string value) => value.Length <= 100 ? value : value[..100] + "…";

    public void Dispose() => _data.Dispose();
}
