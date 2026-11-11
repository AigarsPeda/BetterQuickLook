using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace BetterQuickLook;

internal sealed class WorkspacePreviewView : Grid, IDisposable
{
    private static readonly SemaphoreSlim Checks = new(1, 1);
    private readonly WorkspacePreviewData _data;
    private readonly CancellationTokenSource _cancellation = new();
    private JsonPreviewView? _jsonView;
    private bool _disposed;
    internal Button JsonButton { get; }
    internal Task AvailabilityTask { get; }

    internal WorkspacePreviewView(string path, WorkspacePreviewData data, Style buttonStyle)
    {
        _data = data;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 14, 20, 8) };
        var summaryButton = ActionButton("Workspace", buttonStyle);
        JsonButton = ActionButton("JSON", buttonStyle);
        toolbar.Children.Add(summaryButton);
        toolbar.Children.Add(JsonButton);
        Children.Add(toolbar);
        var body = new Grid();
        SetRow(body, 1);
        Children.Add(body);
        var summary = new StackPanel { Margin = new Thickness(24, 10, 24, 20) };
        var scroll = new ScrollViewer { Content = summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(scroll);
        summary.Children.Add(Label($"{data.FolderCount:N0} folders", 21));
        summary.Children.Add(Label($"{data.Settings:N0} settings  •  {data.Tasks:N0} tasks  •  {data.LaunchConfigurations:N0} debug configurations", 13));
        if (data.Notice is { } notice) summary.Children.Add(Label(notice, 13));
        if (data.FolderCount == 0) summary.Children.Add(Label("No folders to display. Use JSON to inspect the workspace.", 14));

        var statuses = new List<(WorkspaceFolder Folder, TextBlock Label)>();
        foreach (var folder in data.Folders)
        {
            var card = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            card.Children.Add(Label(folder.Name, 16));
            card.Children.Add(new TextBox
            {
                Text = folder.Location, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Brushes.LightGray, Padding = new Thickness(0), FontSize = 12,
                Margin = new Thickness(0, 2, 0, 6)
            });
            var status = Label(folder.Notice ?? "Checking availability…", 12);
            card.Children.Add(status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            if (folder.LocalPath is { } localPath)
            {
                var open = ActionButton("Open folder", buttonStyle);
                open.Click += async (_, _) =>
                {
                    open.IsEnabled = false;
                    try { await Task.Run(() => { using var process = Process.Start(ExplorerStartInfo(localPath)); }); }
                    catch (Exception error) { if (!_disposed) status.Text = "Could not open folder: " + error.Message; }
                    finally { if (!_disposed) open.IsEnabled = true; }
                };
                actions.Children.Add(open);
            }
            var copy = ActionButton("Copy path", buttonStyle);
            copy.Click += (_, _) =>
            {
                try { System.Windows.Clipboard.SetText(folder.Location); }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Clipboard is busy. Try copying again."; }
            };
            actions.Children.Add(copy);
            card.Children.Add(actions);
            summary.Children.Add(new Border
            {
                Child = card, Padding = new Thickness(14, 0, 14, 14), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(38, 43, 51)), Margin = new Thickness(0, 10, 0, 0)
            });
            if (folder.LocalPath is not null && folder.Notice is null) statuses.Add((folder, status));
        }
        summary.Children.Add(Label($"Recommended extensions ({data.ExtensionCount:N0})", 16));
        summary.Children.Add(Label(data.Extensions.Length == 0 ? "None listed" : string.Join("\n", data.Extensions), 13));
        if (data.ExtensionCount > data.Extensions.Length) summary.Children.Add(Label("Showing up to 30 recommendations. Use JSON for the full list.", 12));

        summaryButton.IsEnabled = false;
        summaryButton.Click += (_, _) =>
        {
            scroll.Visibility = Visibility.Visible;
            if (_jsonView is not null) _jsonView.Visibility = Visibility.Collapsed;
            summaryButton.IsEnabled = false;
            JsonButton.IsEnabled = true;
        };
        JsonButton.Click += (_, _) =>
        {
            if (_disposed) return;
            if (_jsonView is null) { _jsonView = new JsonPreviewView(path, data.Json); body.Children.Add(_jsonView); }
            _jsonView.Visibility = Visibility.Visible;
            scroll.Visibility = Visibility.Collapsed;
            JsonButton.IsEnabled = false;
            summaryButton.IsEnabled = true;
        };
        AvailabilityTask = CheckAvailabilityAsync(statuses, _cancellation.Token);
    }

    private async Task CheckAvailabilityAsync(List<(WorkspaceFolder Folder, TextBlock Label)> items, CancellationToken token)
    {
        try
        {
            await Checks.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var results = await Task.Run(() => items.Select(item =>
                {
                    token.ThrowIfCancellationRequested();
                    return Directory.Exists(item.Folder.LocalPath!);
                }).ToArray(), token).ConfigureAwait(false);
                if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    for (var i = 0; i < results.Length; i++) items[i].Label.Text = results[i] ? "Available" : "Missing or inaccessible";
                });
            }
            finally { Checks.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }

    internal static ProcessStartInfo ExplorerStartInfo(string folder)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { UseShellExecute = true };
        start.ArgumentList.Add(Path.GetFullPath(folder));
        return start;
    }

    private static Button ActionButton(string text, Style style) => new()
    { Content = text, Style = style, Margin = new Thickness(0, 0, 8, 0) };
    private static TextBlock Label(string text, double size) => new()
    { Text = text, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 3) };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        if (_jsonView is not null) _jsonView.Dispose();
        else _data.Dispose();
    }
}
