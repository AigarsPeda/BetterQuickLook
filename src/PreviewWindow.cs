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
    private readonly TextBlock _preferredApp;
    private readonly WpfButton _openButton;
    private readonly WpfButton _changeProgramButton;
    private readonly string? _settingsPath;
    private ShellPreview? _shellPreview;
    private JsonPreviewView? _jsonView;
    private CancellationTokenSource? _folderScan;
    private int _request;
    private bool _allowClose;
    private bool _programMenuOpen;
    private bool _programDialogOpen;
    private string? _currentFilePath;
    private int _iconRequest;
    private int _launchRequest;

    internal bool IsChoosingProgram => _programMenuOpen || _programDialogOpen;
    internal bool IsProgramButtonFocused => _openButton.IsKeyboardFocusWithin || _changeProgramButton.IsKeyboardFocusWithin;
    internal bool CanOpenFile => _openButton.Visibility == Visibility.Visible;
    internal string PreferredAppLabel => _preferredApp.Text;
    internal WpfButton OpenButton => _openButton;
    internal WpfButton ChangeProgramButton => _changeProgramButton;
    internal WpfContextMenu? ProgramMenu { get; private set; }

    public PreviewWindow(string? settingsPath = null)
    {
        _settingsPath = settingsPath;
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
        _preferredApp = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(166, 174, 187)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 190,
            Margin = new Thickness(8, 0, 10, 0),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(_preferredApp, 1);
        footer.Children.Add(_preferredApp);
        _openButton = MakeFooterButton("Choose app");
        _openButton.Click += (_, _) => OpenCurrentFile();
        Grid.SetColumn(_openButton, 2);
        footer.Children.Add(_openButton);
        _changeProgramButton = MakeFooterButton("");
        _changeProgramButton.Click += (_, _) => ShowProgramChoices(_changeProgramButton);
        _changeProgramButton.Padding = new Thickness(7, 5, 7, 5);
        _changeProgramButton.MinWidth = 34;
        _changeProgramButton.MinHeight = 30;
        _changeProgramButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_changeProgramButton, 3);
        footer.Children.Add(_changeProgramButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        SizeChanged += (_, _) =>
        {
            hint.Visibility = ActualWidth < 740 ? Visibility.Collapsed : Visibility.Visible;
            _preferredApp.MaxWidth = ActualWidth < 600 ? 120 : 190;
        };
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
        _currentFilePath = null;
        _iconRequest++;
        _launchRequest++;
        _preferredApp.Visibility = Visibility.Collapsed;
        _openButton.Visibility = Visibility.Collapsed;
        _changeProgramButton.Visibility = Visibility.Collapsed;
        _openButton.IsEnabled = true;
        _changeProgramButton.IsEnabled = true;

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
        _currentFilePath = path;
        _ = ShellFileHandlers.ListAsync(ext);
        RefreshPreferredApp();
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

    private async void RefreshPreferredApp()
    {
        if (_currentFilePath is null) return;
        var filePath = _currentFilePath;
        var iconRequest = ++_iconRequest;
        var extension = Path.GetExtension(filePath);
        var selection = FileOpener.LoadSelection(extension, _settingsPath);
        _preferredApp.Text = selection is null ? "Preferred app: none" : $"Preferred app: {selection.DisplayName}";
        _preferredApp.ToolTip = _preferredApp.Text;
        _preferredApp.Visibility = Visibility.Visible;
        _openButton.Content = selection is null ? "Choose app" : "Open";
        _openButton.Visibility = Visibility.Visible;
        _changeProgramButton.Content = selection is null
            ? new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16 }
            : ProgramIcon(ProgramIcons.Fallback);
        _changeProgramButton.ToolTip = selection is null
            ? "Choose a program"
            : $"Change program (currently {selection.DisplayName})";
        _changeProgramButton.Visibility = Visibility.Visible;
        if (selection is null) return;

        BitmapSource? icon;
        if (selection.HandlerName is { } handlerName)
        {
            var handlers = await ShellFileHandlers.ListAsync(extension);
            icon = handlers.FirstOrDefault(handler => ShellFileHandlers.Matches(handler, handlerName, selection.DisplayName))?.Icon;
            if (icon is null) icon = await ProgramIcons.ExtractAsync(handlerName);
        }
        else icon = await ProgramIcons.ExtractAsync(selection.ExecutablePath);

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_currentFilePath == filePath && iconRequest == _iconRequest)
                _changeProgramButton.Content = ProgramIcon(icon ?? ProgramIcons.Fallback);
        }));
    }

    private static WpfImage ProgramIcon(BitmapSource icon) => new()
    {
        Source = icon,
        Width = 18,
        Height = 18,
        Stretch = Stretch.Uniform
    };

    private void OpenCurrentFile()
    {
        if (_currentFilePath is null) return;
        var extension = Path.GetExtension(_currentFilePath);
        var selection = FileOpener.LoadSelection(extension, _settingsPath);
        if (selection is null) ShowProgramChoices(_openButton);
        else LaunchProgram(selection);
    }

    private async void ShowProgramChoices(WpfButton anchor)
    {
        if (_currentFilePath is null) return;
        var filePath = _currentFilePath;
        var extension = Path.GetExtension(filePath);
        var menu = new WpfContextMenu
        {
            PlacementTarget = anchor,
            Background = new SolidColorBrush(Color.FromRgb(38, 43, 51)),
            Foreground = Brushes.White,
            MaxHeight = 460
        };
        ProgramMenu = menu;
        menu.Items.Add(new WpfMenuItem { Header = "Finding available programs…", IsEnabled = false });
        var browseItem = new WpfMenuItem { Header = "Choose another program…" };
        browseItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(BrowseForProgram);
        };
        menu.Items.Add(browseItem);
        menu.Opened += (_, _) => _programMenuOpen = true;
        menu.Closed += (_, _) => _programMenuOpen = false;
        menu.IsOpen = true;

        var handlers = await ShellFileHandlers.ListAsync(extension);
        _ = Dispatcher.BeginInvoke(new Action(() => PopulateProgramMenu(menu, browseItem, filePath, extension, handlers)));
    }

    private void PopulateProgramMenu(WpfContextMenu menu, WpfMenuItem browseItem, string filePath,
        string extension, IReadOnlyList<ShellFileHandlers.Handler> handlers)
    {
        if (!menu.IsOpen || _currentFilePath != filePath) return;
        menu.Items.Clear();
        var current = FileOpener.LoadSelection(extension, _settingsPath);
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
                Icon = ProgramIcon(handler.Icon ?? ProgramIcons.Fallback),
                IsChecked = current?.HandlerName is { } name && ShellFileHandlers.Matches(handler, name, current.DisplayName)
            };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => ChooseHandler(handler, filePath));
            };
            menu.Items.Add(item);
        }
        var paint = IsLikelyImage(extension) ? FileOpener.FindPaint() : null;
        if (paint is not null && !handlers.Any(handler => handler.Name.Contains("mspaint", StringComparison.OrdinalIgnoreCase)))
        {
            var paintItem = new WpfMenuItem
            {
                Header = "Paint",
                Icon = ProgramIcon(ProgramIcons.Fallback)
            };
            paintItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => ChooseExecutable(paint));
            };
            menu.Items.Add(paintItem);
            _ = ProgramIcons.ExtractAsync(paint).ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (menu.IsOpen && task.IsCompletedSuccessfully && task.Result is { } icon)
                    paintItem.Icon = ProgramIcon(icon);
            })));
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(browseItem);
    }

    private void BrowseForProgram()
    {
        _programDialogOpen = true;
        try
        {
            var dialog = new EditorFileDialog
            {
                Title = "Choose a program",
                Filter = "Programs (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true) ChooseExecutable(dialog.FileName);
        }
        finally { _programDialogOpen = false; }
    }

    private void ChooseExecutable(string executablePath)
    {
        if (_currentFilePath is null) return;
        try
        {
            FileOpener.SaveExecutable(Path.GetExtension(_currentFilePath), executablePath, _settingsPath);
            RefreshPreferredApp();
            LaunchProgram(FileOpener.LoadSelection(Path.GetExtension(_currentFilePath), _settingsPath)!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not save program choice");
        }
    }

    private void ChooseHandler(ShellFileHandlers.Handler handler, string filePath)
    {
        if (_currentFilePath != filePath) return;
        try
        {
            FileOpener.SaveHandler(Path.GetExtension(filePath), handler.Name, handler.DisplayName, _settingsPath);
            RefreshPreferredApp();
            LaunchProgram(new FileOpener.Selection(null, handler.Name, handler.DisplayName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not save program choice");
        }
    }

    private void LaunchProgram(FileOpener.Selection selection)
    {
        if (_currentFilePath is null) return;
        var filePath = _currentFilePath;
        var request = ++_launchRequest;
        _openButton.IsEnabled = false;
        _changeProgramButton.IsEnabled = false;
        _openButton.Content = "Opening…";
        Task launch = selection.HandlerName is { } handlerName
            ? ShellFileHandlers.OpenAsync(Path.GetExtension(filePath), handlerName, selection.DisplayName, filePath)
            : Task.Run(() => Process.Start(FileOpener.CreateStartInfo(selection.ExecutablePath!, filePath)));
        _ = launch.ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            var error = task.Exception?.GetBaseException();
            if (request != _launchRequest || _currentFilePath != filePath) return;
            _openButton.IsEnabled = true;
            _changeProgramButton.IsEnabled = true;
            if (error is null) { Hide(); return; }
            RefreshPreferredApp();
            if (error is FileNotFoundException && selection.HandlerName is not null)
                ShowProgramChoices(_changeProgramButton);
            else
                System.Windows.MessageBox.Show(this, error.Message, "Could not open file");
        })));
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

    private static bool IsLikelyImage(string ext) => FileOpener.IsImageExtension(ext);

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
        _iconRequest++;
        _launchRequest++;
        _currentFilePath = null;
        _preferredApp.Visibility = Visibility.Collapsed;
        _openButton.Visibility = Visibility.Collapsed;
        _changeProgramButton.Visibility = Visibility.Collapsed;
        _openButton.IsEnabled = true;
        _changeProgramButton.IsEnabled = true;
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
