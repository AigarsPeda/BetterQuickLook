using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace BetterQuickLook;

internal sealed partial class PreviewWindow
{
    private Button _backButton = null!;
    private readonly List<string> _navigation = [];
    private string? _displayedPath;
    internal Button BackButton => _backButton;

    private void ClearNavigation()
    {
        _navigation.Clear();
        _displayedPath = null;
        _backButton.Visibility = Visibility.Collapsed;
    }

    private void NavigateTo(string path)
    {
        if (_displayedPath is { } previous)
        {
            if (_navigation.Count == 32) _navigation.RemoveAt(0);
            _navigation.Add(previous);
        }
        _backButton.Visibility = Visibility.Visible;
        ShowFileCore(path);
    }

    private void NavigateBack()
    {
        if (_navigation.Count == 0) return;
        var path = _navigation[^1];
        _navigation.RemoveAt(_navigation.Count - 1);
        _backButton.Visibility = _navigation.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowFileCore(path);
    }

    private void ShowFolder(string path)
    {
        var panel = new StackPanel { Margin = new Thickness(22, 16, 22, 18) };
        AddFolderLabel(panel, "LOCATION");
        panel.Children.Add(new TextBox
        {
            Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Background = Brushes.Transparent, Foreground = Brushes.White, FontSize = 14,
            Margin = new Thickness(0, 5, 0, 12)
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var open = MakeFooterButton(); open.Content = "Open folder";
        var copy = MakeFooterButton(); copy.Content = "Copy path"; copy.Margin = new Thickness(8, 0, 0, 0);
        open.Visibility = copy.Visibility = Visibility.Visible;
        actions.Children.Add(open); actions.Children.Add(copy); panel.Children.Add(actions);
        var actionStatus = new TextBlock { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(actionStatus);
        var request = _request;
        open.Click += async (_, _) =>
        {
            open.IsEnabled = false; open.Content = "Opening…";
            try { await Task.Run(() => { using var process = Process.Start(WorkspacePreviewView.ExplorerStartInfo(path)); }); }
            catch (Exception error) { if (request == _request) actionStatus.Text = "Could not open folder: " + error.Message; }
            finally { if (request == _request) { open.IsEnabled = true; open.Content = "Open folder"; } }
        };
        copy.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(path); actionStatus.Text = "Path copied"; }
            catch (System.Runtime.InteropServices.ExternalException) { actionStatus.Text = "Clipboard is busy. Try again."; }
        };
        var stats = new Grid();
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var sizePanel = new StackPanel(); var countPanel = new StackPanel();
        Grid.SetColumn(countPanel, 1); stats.Children.Add(sizePanel); stats.Children.Add(countPanel);
        AddFolderLabel(sizePanel, "SIZE"); var size = AddFolderValue(sizePanel, "Calculating…");
        AddFolderLabel(countPanel, "TOTAL CONTENTS"); var count = AddFolderValue(countPanel, "Counting…");
        panel.Children.Add(stats);
        var note = new TextBlock { Foreground = Brushes.LightGray, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(note);
        AddFolderLabel(panel, "INSIDE THIS FOLDER");
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        list.Children.Add(new TextBlock { Text = "Loading contents…", Foreground = Brushes.LightGray });
        panel.Children.Add(list);
        _body.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _ = LoadPreviewAsync(() => FolderContents.Read(path, CancellationToken.None), result =>
        {
            list.Children.Clear();
            foreach (var item in result.Items)
            {
                var row = MakeFooterButton();
                row.Visibility = Visibility.Visible;
                row.Height = 46;
                row.Tag = item.Path;
                row.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
                row.Margin = new Thickness(0, 0, 0, 4);
                row.ToolTip = item.Path;
                System.Windows.Automation.AutomationProperties.SetName(row, "Preview " + item.Name);
                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                content.ColumnDefinitions.Add(new ColumnDefinition());
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                var icon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(item.IsFolder
                        ? "M1,4 L6,4 L8,6 L17,6 L17,15 L1,15 Z M1,4 L1,2 L7,2 L9,4 L16,4 L16,6"
                        : "M4,1 L11,1 L16,6 L16,17 L4,17 Z M11,1 L11,6 L16,6 M7,10 L13,10 M7,13 L13,13"),
                    Stroke = item.IsFolder ? Brushes.Khaki : Brushes.LightGray, StrokeThickness = 1.2,
                    Width = 18, Height = 18, Stretch = Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
                };
                content.Children.Add(icon);
                var name = new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1); content.Children.Add(name);
                var metadata = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                metadata.Children.Add(new TextBlock { Text = item.IsFolder ? "Folder" : FormatSize(item.Bytes), FontSize = 12 });
                metadata.Children.Add(new TextBlock { Text = item.Modified.ToString("d"), FontSize = 11, Foreground = Brushes.LightGray, ToolTip = item.Modified.ToString("g") });
                Grid.SetColumn(metadata, 2); content.Children.Add(metadata);
                row.Content = content;
                row.Click += (_, _) => { if (request == _request) NavigateTo(item.Path); };
                list.Children.Add(row);
            }
            list.Children.Add(new TextBlock
            {
                Text = result.Items.Length == 0 ? "This folder is empty." : result.HasMore
                    ? $"Showing the first {FolderContents.Limit} items. Open folder to see everything."
                    : "Click an item to preview it here.",
                Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
            });
        }, "Could not list this folder. It may have moved or become inaccessible.");
        LoadFolderDetails(path, size, count, note, request);
    }
}
