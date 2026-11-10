using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;
using WpfImage = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using TextBox = System.Windows.Controls.TextBox;

namespace BetterQuickLook;

internal sealed class PreviewWindow : Window
{
    private readonly TextBlock _title;
    private readonly TextBlock _details;
    private readonly Grid _body;
    private ShellPreview? _shellPreview;
    private CancellationTokenSource? _folderScan;
    private int _request;
    private bool _allowClose;

    public PreviewWindow()
    {
        Title = "BetterQuickLook";
        Width = 900;
        Height = 650;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(22, 25, 31));
        Foreground = Brushes.White;
        ShowInTaskbar = true;
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Space) Hide(); };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var header = new Grid { Margin = new Thickness(22, 17, 22, 13) };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _details = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(166, 174, 187)), Margin = new Thickness(0, 5, 0, 0) };
        header.Children.Add(_title);
        Grid.SetRow(_details, 1);
        header.Children.Add(_details);
        root.Children.Add(header);

        _body = new Grid { Margin = new Thickness(18, 0, 18, 0), Background = new SolidColorBrush(Color.FromRgb(31, 35, 42)) };
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);

        var footer = new TextBlock
        {
            Text = "Space or Esc to close  •  Select a file in File Explorer to preview it",
            Foreground = new SolidColorBrush(Color.FromRgb(130, 139, 151)),
            FontSize = 11,
            Margin = new Thickness(22, 10, 22, 12)
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        // Windows 10 used attribute 19; current Windows uses attribute 20.
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void ShowFile(string path)
    {
        _request++;
        CancelFolderScan();
        _shellPreview?.Dispose();
        _shellPreview = null;
        _body.Children.Clear();

        var info = new FileInfo(path);
        _title.Text = info.Name;
        _details.Text = Directory.Exists(path)
            ? $"Folder  •  Modified {Directory.GetLastWriteTime(path):g}"
            : $"{DescribeType(info.Extension)}  •  {FormatSize(info.Length)}  •  Modified {info.LastWriteTime:g}";

        WindowState = WindowState.Normal;
        Show();
        Topmost = false;
        Topmost = true;
        Activate();
        if (Directory.Exists(path)) { ShowFolder(path); return; }

        var ext = info.Extension.ToLowerInvariant();
        if (ext is ".docx" or ".xlsx")
        {
            ShowMessage("Loading document preview…");
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!IsVisible) return;
                if (!ShowNativePreview(path)) ShowDocumentText(path);
            });
            return;
        }

        if (IsLikelyImage(ext))
        {
            ShowMessage("Loading image…");
            var current = _request;
            _ = Task.Run(() => DecodeImage(path)).ContinueWith(task =>
                Dispatcher.Invoke(() =>
                {
                    if (current != _request || !IsVisible) return;
                    if (task.IsCompletedSuccessfully && task.Result is { } image)
                        ShowImage(image);
                    else
                        ShowMessage("No image decoder is installed for this format.");
                }));
            return;
        }

        // The native handler supplies rich previews for Office's older .doc/.xls formats.
        if (ext is ".doc" or ".xls" or ".docm" or ".xlsm" or ".xlsb")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!IsVisible) return;
                if (!ShowNativePreview(path)) ShowMessage("No preview handler is installed for this format.");
            });
            return;
        }

        ShowMessage(path);
    }

    private void ShowFolder(string path)
    {
        var panel = new StackPanel { Margin = new Thickness(30), MaxWidth = 760 };
        AddFolderLabel(panel, "LOCATION");
        panel.Children.Add(new TextBox
        {
            Text = path,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            FontSize = 17,
            Margin = new Thickness(0, 7, 0, 28)
        });
        AddFolderLabel(panel, "SIZE");
        var size = AddFolderValue(panel, "Calculating…");
        AddFolderLabel(panel, "CONTENTS");
        var contents = AddFolderValue(panel, "Counting files and folders…");
        var note = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(166, 174, 187)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(note);
        _body.Children.Add(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        LoadFolderDetails(path, size, contents, note, _request);
    }

    private static void AddFolderLabel(System.Windows.Controls.Panel panel, string label) => panel.Children.Add(new TextBlock
    {
        Text = label,
        Foreground = new SolidColorBrush(Color.FromRgb(166, 174, 187)),
        FontSize = 12,
        FontWeight = FontWeights.SemiBold
    });

    private static TextBlock AddFolderValue(System.Windows.Controls.Panel panel, string value)
    {
        var text = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 28)
        };
        panel.Children.Add(text);
        return text;
    }

    private void LoadFolderDetails(string path, TextBlock size, TextBlock contents, TextBlock note, int request)
    {
        var scan = new CancellationTokenSource();
        _folderScan = scan;
        _ = Task.Run(() =>
        {
            FolderScanResult? result = null;
            try { result = FolderPreview.Scan(path, scan.Token); }
            catch (OperationCanceledException) { }
            catch (Exception) { }

            if (Dispatcher.HasShutdownStarted)
            {
                scan.Dispose();
                return;
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_folderScan, scan)) _folderScan = null;
                if (request == _request && IsVisible)
                {
                    if (result is { } summary)
                    {
                        size.Text = (summary.IsPartial ? "At least " : "") + FormatSize(summary.Bytes);
                        contents.Text = $"{summary.Files:N0} files  •  {summary.Folders:N0} folders" +
                                        (summary.IsPartial ? " (partial)" : "");
                        if (summary.ReachedLimit) note.Text = "Scan stopped after 100,000 items to limit resource use.";
                        else if (summary.Skipped > 0) note.Text = "Linked or inaccessible items were excluded from the size.";
                    }
                    else if (!scan.IsCancellationRequested)
                    {
                        size.Text = "Unavailable";
                        contents.Text = "Could not read this folder";
                    }
                }
                scan.Dispose();
            });
        });
    }

    private void CancelFolderScan()
    {
        _folderScan?.Cancel();
        _folderScan = null;
    }

    private bool ShowNativePreview(string path)
    {
        var panel = new WinForms.Panel { BackColor = System.Drawing.Color.White };
        var host = new System.Windows.Forms.Integration.WindowsFormsHost { Child = panel };
        _body.Children.Clear();
        _body.Children.Add(host);
        try { _shellPreview = ShellPreview.TryOpen(path, panel.Handle); }
        catch { _shellPreview = null; }
        if (_shellPreview is not null) return true;
        _body.Children.Clear();
        host.Dispose();
        return false;
    }

    private void ShowDocumentText(string path)
    {
        ShowMessage("Loading document text…");
        var current = _request;
        _ = Task.Run(() => DocumentPreview.Read(path)).ContinueWith(task =>
            Dispatcher.Invoke(() =>
            {
                if (current != _request || !IsVisible) return;
                if (task.IsCompletedSuccessfully && task.Result is { } preview)
                    ShowText(preview);
                else
                    ShowMessage("Could not read this document. File details are shown above.");
            }));
    }

    private void ShowImage(BitmapSource image)
    {
        _body.Children.Clear();
        _body.Children.Add(new WpfImage
        {
            Source = image,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(10)
        });
    }

    private void ShowText(string text)
    {
        _body.Children.Clear();
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Padding = new Thickness(22),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        _body.Children.Add(box);
    }

    private void ShowMessage(string message)
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            MaxWidth = 680,
            FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 197, 207)),
            Margin = new Thickness(25)
        });
    }

    internal static BitmapSource DecodeImage(string path)
    {
        int width, height;
        using (var header = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var decoder = BitmapDecoder.Create(header, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if (Math.Max(width, height) > 1800)
        {
            if (width >= height) bitmap.DecodePixelWidth = 1800;
            else bitmap.DecodePixelHeight = 1800;
        }
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsLikelyImage(string ext) => ext is
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" or
        ".webp" or ".heic" or ".heif" or ".avif" or ".ico" or ".wdp" or ".jxr";

    private static string DescribeType(string ext) => string.IsNullOrEmpty(ext)
        ? "File" : ext.TrimStart('.').ToUpperInvariant() + " file";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double size = bytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }

    public new void Hide()
    {
        _request++;
        CancelFolderScan();
        _shellPreview?.Dispose();
        _shellPreview = null;
        _body.Children.Clear();
        base.Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }
}
