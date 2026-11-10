using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
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
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using EditorFileDialog = Microsoft.Win32.OpenFileDialog;

namespace BetterQuickLook;

internal sealed class PreviewWindow : Window
{
    private readonly TextBlock _title;
    private readonly TextBlock _details;
    private readonly Grid _body;
    private readonly WpfButton _editButton;
    private readonly WpfButton _changeEditorButton;
    private ShellPreview? _shellPreview;
    private JsonPreviewView? _jsonView;
    private CancellationTokenSource? _folderScan;
    private int _request;
    private bool _allowClose;
    private bool _editorMenuOpen;
    private bool _editorDialogOpen;
    private string? _currentImagePath;

    internal bool IsChoosingEditor => _editorMenuOpen || _editorDialogOpen;
    internal bool IsEditorButtonFocused => _editButton.IsKeyboardFocusWithin || _changeEditorButton.IsKeyboardFocusWithin;
    internal bool CanEditImage => _editButton.Visibility == Visibility.Visible;
    internal WpfButton ChangeProgramButton => _changeEditorButton;
    internal WpfContextMenu? EditorMenu { get; private set; }

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

        var footer = new Grid { Margin = new Thickness(22, 8, 22, 10) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock
        {
            Text = "Space or Esc to close  •  Select a file in File Explorer to preview it",
            Foreground = new SolidColorBrush(Color.FromRgb(130, 139, 151)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(hint);
        _editButton = MakeFooterButton("Edit image");
        _editButton.Click += (_, _) => OpenImageInEditor();
        Grid.SetColumn(_editButton, 1);
        footer.Children.Add(_editButton);
        _changeEditorButton = MakeFooterButton("Change program");
        _changeEditorButton.Click += (_, _) => ShowEditorChoices(_changeEditorButton);
        _changeEditorButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_changeEditorButton, 2);
        footer.Children.Add(_changeEditorButton);
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
        _jsonView?.Dispose();
        _jsonView = null;
        _currentImagePath = null;
        _editButton.Visibility = Visibility.Collapsed;
        _changeEditorButton.Visibility = Visibility.Collapsed;

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
        if (IsLikelyImage(ext))
        {
            _currentImagePath = path;
            _ = ShellImageHandlers.ListAsync(ext);
            var chosenEditor = ImageEditor.LoadSelection();
            _editButton.Content = chosenEditor is null ? "Open with…" : $"Open in {chosenEditor.DisplayName}";
            _editButton.Visibility = Visibility.Visible;
            _changeEditorButton.Visibility = Visibility.Visible;
        }
        if (ext is ".json" or ".jsonc")
        {
            ShowJson(path);
            return;
        }
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

    private static WpfButton MakeFooterButton(string label) => new()
    {
        Content = label,
        Visibility = Visibility.Collapsed,
        Background = new SolidColorBrush(Color.FromRgb(50, 64, 84)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(78, 96, 119)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 6, 12, 6),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private void OpenImageInEditor()
    {
        if (_currentImagePath is null) return;
        var editor = ImageEditor.LoadSelection();
        if (editor is null) ShowEditorChoices(_editButton);
        else LaunchEditor(editor);
    }

    private async void ShowEditorChoices(WpfButton anchor)
    {
        if (_currentImagePath is null) return;
        var imagePath = _currentImagePath;
        var menu = new WpfContextMenu
        {
            PlacementTarget = anchor,
            Background = new SolidColorBrush(Color.FromRgb(38, 43, 51)),
            Foreground = Brushes.White,
            MaxHeight = 460
        };
        EditorMenu = menu;
        menu.Items.Add(new WpfMenuItem { Header = "Finding available programs…", IsEnabled = false });
        var browseItem = new WpfMenuItem { Header = "Choose another program…" };
        browseItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(BrowseForEditor);
        };
        menu.Items.Add(browseItem);
        menu.Opened += (_, _) => _editorMenuOpen = true;
        menu.Closed += (_, _) => _editorMenuOpen = false;
        menu.IsOpen = true;

        var handlers = await ShellImageHandlers.ListAsync(Path.GetExtension(imagePath));
        if (!menu.IsOpen || _currentImagePath != imagePath) return;
        menu.Items.Clear();
        var current = ImageEditor.LoadSelection();
        var showedOther = false;
        foreach (var handler in handlers)
        {
            if (!handler.Recommended && !showedOther && menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
                showedOther = true;
            }
            var item = new WpfMenuItem
            {
                Header = handler.DisplayName,
                IsChecked = current?.HandlerName is { } name && ShellImageHandlers.Matches(handler, name, current.DisplayName)
            };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => ChooseHandler(handler));
            };
            menu.Items.Add(item);
        }
        var paint = ImageEditor.FindPaint();
        if (paint is not null && !handlers.Any(handler => handler.Name.Contains("mspaint", StringComparison.OrdinalIgnoreCase)))
        {
            var paintItem = new WpfMenuItem { Header = "Paint" };
            paintItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => ChooseEditor(paint));
            };
            menu.Items.Add(paintItem);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(browseItem);
    }

    private void BrowseForEditor()
    {
        _editorDialogOpen = true;
        try
        {
            var dialog = new EditorFileDialog
            {
                Title = "Choose an image editor",
                Filter = "Programs (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true) ChooseEditor(dialog.FileName);
        }
        finally { _editorDialogOpen = false; }
    }

    private void ChooseEditor(string editorPath)
    {
        try
        {
            ImageEditor.Save(editorPath);
            var selection = ImageEditor.LoadSelection()!;
            _editButton.Content = $"Open in {selection.DisplayName}";
            LaunchEditor(selection);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not save editor choice");
        }
    }

    private void ChooseHandler(ShellImageHandlers.Handler handler)
    {
        try
        {
            ImageEditor.SaveHandler(handler.Name, handler.DisplayName);
            _editButton.Content = $"Open in {handler.DisplayName}";
            LaunchEditor(new ImageEditor.Selection(null, handler.Name, handler.DisplayName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not save program choice");
        }
    }

    private void LaunchEditor(ImageEditor.Selection selection)
    {
        if (_currentImagePath is null) return;
        try
        {
            if (selection.HandlerName is { } handlerName)
                ShellImageHandlers.Open(Path.GetExtension(_currentImagePath), handlerName, selection.DisplayName, _currentImagePath);
            else if (selection.EditorPath is { } editorPath)
                Process.Start(ImageEditor.CreateStartInfo(editorPath, _currentImagePath));
            Hide();
        }
        catch (FileNotFoundException) when (selection.HandlerName is not null)
        {
            ShowEditorChoices(_editButton);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or COMException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not open image program");
        }
    }

    private void ShowJson(string path)
    {
        ShowMessage("Loading JSON preview…");
        var current = _request;
        _ = Task.Run(() => JsonPreviewReader.Read(path)).ContinueWith(task =>
            Dispatcher.BeginInvoke(() =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    _ = task.Exception;
                    if (current == _request && IsVisible)
                        ShowMessage("Could not read this JSON file. File details are shown above.");
                    return;
                }
                var data = task.Result;
                if (current != _request || !IsVisible)
                {
                    data.Dispose();
                    return;
                }
                _body.Children.Clear();
                _jsonView = new JsonPreviewView(path, data);
                _body.Children.Add(_jsonView);
            }));
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
        _currentImagePath = null;
        _editButton.Visibility = Visibility.Collapsed;
        _changeEditorButton.Visibility = Visibility.Collapsed;
        CancelFolderScan();
        _shellPreview?.Dispose();
        _shellPreview = null;
        _body.Children.Clear();
        _jsonView?.Dispose();
        _jsonView = null;
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
