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
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using EditorFileDialog = Microsoft.Win32.OpenFileDialog;

namespace BetterQuickLook;

internal sealed partial class PreviewWindow : Window
{
    private readonly TextBlock _title;
    private readonly TextBlock _details;
    private readonly TextBlock _shortcutDetails;
    private readonly Grid _body;
    private readonly TextBlock _openLabel;
    private readonly TextBlock _openingLabel;
    private readonly WpfButton _openButton;
    private readonly WpfButton _changeProgramButton;
    private readonly string? _settingsPath;
    private ShellPreview? _shellPreview;
    private System.Windows.Forms.Integration.WindowsFormsHost? _nativeHost;
    private JsonPreviewView? _jsonView;
    private CancellationTokenSource? _folderScan;
    private CancellationTokenSource? _previewLoad;
    private int _request;
    private bool _allowClose;
    private bool _programDialogOpen;
    private bool _isLaunchingProgram;
    private string? _currentFilePath;
    private int _launchRequest;

    internal bool IsChoosingProgram => ProgramMenu?.IsOpen == true || _programDialogOpen;
    internal bool IsProgramButtonFocused => _openButton.IsKeyboardFocusWithin || _changeProgramButton.IsKeyboardFocusWithin;
    internal bool CanOpenFile => _openButton.Visibility == Visibility.Visible;
    internal string OpenButtonLabel => _openLabel.Text;
    internal WpfButton OpenButton => _openButton;
    internal WpfButton ChangeProgramButton => _changeProgramButton;
    internal WpfContextMenu? ProgramMenu { get; private set; }

    public PreviewWindow(string? settingsPath = null)
    {
        _settingsPath = settingsPath;
        // Load preview-only resources on the first preview, not during tray startup.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/BetterQuickLook;component/ScrollBarTheme.xaml", UriKind.Relative)
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/BetterQuickLook;component/PreviewControls.xaml", UriKind.Relative)
        });
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
        var openCommand = new RoutedCommand();
        InputBindings.Add(new KeyBinding(openCommand, Key.Enter, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(openCommand, (_, _) => OpenCurrentFile(),
            (_, e) => e.CanExecute = CanOpenCurrentFile));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var header = new Grid { Margin = new Thickness(22, 17, 22, 13) };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _details = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(166, 174, 187)), Margin = new Thickness(0, 5, 0, 0) };
        var heading = new DockPanel();
        _backButton = MakeFooterButton();
        _backButton.Content = "← Back";
        _backButton.Margin = new Thickness(0, 0, 12, 0);
        _backButton.Visibility = Visibility.Collapsed;
        _backButton.Click += (_, _) => NavigateBack();
        DockPanel.SetDock(_backButton, Dock.Left);
        heading.Children.Add(_backButton);
        heading.Children.Add(_title);
        header.Children.Add(heading);
        Grid.SetRow(_details, 1);
        header.Children.Add(_details);
        _shortcutDetails = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brushes.LightGray,
            MaxHeight = 64, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_shortcutDetails, 2);
        header.Children.Add(_shortcutDetails);
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
        _openLabel = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        _openingLabel = new TextBlock { Text = "Opening…", HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Visibility = Visibility.Hidden };
        // Hidden labels still reserve their space, so the footer stays put during a launch.
        var openContent = new Grid();
        openContent.Children.Add(_openLabel);
        openContent.Children.Add(_openingLabel);
        _openButton = MakeFooterButton("PreviewPrimaryButton");
        _openButton.Content = openContent;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_openButton, System.Windows.Automation.AutomationLiveSetting.Polite);
        _openButton.MaxWidth = 250;
        _openButton.Click += (_, _) => OpenCurrentFile();
        Grid.SetColumn(_openButton, 1);
        footer.Children.Add(_openButton);
        _changeProgramButton = MakeFooterButton();
        _changeProgramButton.Content = new System.Windows.Shapes.Path { Style = (Style)FindResource("ProgramPickerIcon") };
        _changeProgramButton.ToolTip = "Open with another app";
        System.Windows.Automation.AutomationProperties.SetName(_changeProgramButton, "Open with another app");
        _changeProgramButton.Click += (_, _) => ShowProgramChoices(_changeProgramButton);
        _changeProgramButton.Padding = new Thickness(0);
        _changeProgramButton.Width = 34;
        _changeProgramButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_changeProgramButton, 2);
        footer.Children.Add(_changeProgramButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        SizeChanged += (_, _) => hint.Visibility = ActualWidth < 740 ? Visibility.Collapsed : Visibility.Visible;
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

    private void ResetContent()
    {
        if (ProgramMenu is { } menu) menu.IsOpen = false;
        _request++;
        _previewLoad?.Cancel();
        CancelFolderScan();
        DisposeNativePreview();
        _body.Children.Clear();
        _jsonView?.Dispose();
        _jsonView = null;
        _workspaceView?.Dispose();
        _workspaceView = null;
        _shellOpenPath = null;
        _shortcutDetails.Text = "";
        _shortcutDetails.ToolTip = null;
        _shortcutDetails.Visibility = Visibility.Collapsed;
        _currentFilePath = null;
        _launchRequest++;
        _openButton.Visibility = Visibility.Collapsed;
        _changeProgramButton.Visibility = Visibility.Collapsed;
        SetProgramLaunching(false);
    }

    internal void ShowSelectionPrompt()
    {
        ClearNavigation();
        ResetContent();
        _title.Text = "No single item selected";
        _details.Text = "";
        ShowMessage("Select one file or folder on the Desktop or in File Explorer.");
    }

    public void ShowFile(string path, bool activate = true)
    {
        ClearNavigation();
        ShowFileCore(path, activate);
    }

    private void ShowFileCore(string path, bool activate = true)
    {
        _displayedPath = path;
        ResetContent();

        // Selection updates must not steal Explorer's arrow keys, move the
        // window, or restore a preview the user has minimized.
        if (!IsVisible)
        {
            ShowActivated = activate;
            WindowState = WindowState.Normal;
            Show();
            Topmost = true;
        }
        if (activate) Activate();

        var info = new FileInfo(path);
        _title.Text = info.Name;
        try
        {
            _details.Text = Directory.Exists(path)
                ? $"Folder  •  Modified {Directory.GetLastWriteTime(path):g}"
                : $"{DescribeType(info.Extension)}  •  {FormatSize(info.Length)}  •  Modified {info.LastWriteTime:g}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _details.Text = "File unavailable";
            ShowMessage("This item may have been moved, deleted, or become unavailable.");
            return;
        }
        if (info.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            ShowMessage("Reading shortcut…");
            _ = LoadPreviewAsync(() => ShortcutPreview.Read(path), ShowShortcut,
                "Could not read this shortcut.", sta: true);
            return;
        }
        ShowContent(path);
    }

    private void ShowContent(string path)
    {
        if (Directory.Exists(path)) { ShowFolder(path); return; }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        _currentFilePath = path;
        RefreshPreferredApp();
        if (ext == ".code-workspace")
        {
            ShowMessage("Loading workspace…");
            _ = LoadPreviewAsync(() => WorkspacePreview.Read(path), data =>
            {
                _body.Children.Clear();
                _workspaceView = new WorkspacePreviewView(path, data, (Style)FindResource("PreviewFooterButton"));
                _body.Children.Add(_workspaceView);
            }, "Could not read this workspace.");
            return;
        }
        if (ext is ".json" or ".jsonc")
        {
            ShowJson(path);
            return;
        }
        if (ext is ".docx" or ".xlsx")
        {
            ShowMessage("Loading document preview…");
            var current = _request;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (current != _request || !IsVisible) return;
                if (!ShowNativePreview(path)) ShowDocumentText(path);
            });
            return;
        }

        if (IsLikelyImage(ext))
        {
            ShowMessage("Loading image…");
            _ = LoadPreviewAsync(() => DecodeImage(path), ShowImage, "No image decoder is installed for this format.");
            return;
        }

        // The native handler supplies rich previews for Office's older .doc/.xls formats.
        if (ext is ".doc" or ".xls" or ".docm" or ".xlsm" or ".xlsb")
        {
            var current = _request;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (current != _request || !IsVisible) return;
                if (!ShowNativePreview(path)) ShowMessage("No preview handler is installed for this format.");
            });
            return;
        }

        ShowMessage(path);
    }

    private WpfButton MakeFooterButton(string style = "PreviewFooterButton") => new()
    {
        Style = (Style)FindResource(style),
        Visibility = Visibility.Collapsed
    };

    private void RefreshPreferredApp()
    {
        if (_currentFilePath is null) return;
        var extension = Path.GetExtension(_currentFilePath);
        var selection = FileOpener.LoadSelection(extension, _settingsPath);
        _openLabel.Text = selection is null ? "Choose app" : $"Open in {selection.DisplayName}";
        _openButton.ToolTip = $"{_openLabel.Text} (Ctrl+Enter)";
        System.Windows.Automation.AutomationProperties.SetName(_openButton, _openLabel.Text);
        _openButton.Visibility = Visibility.Visible;
        _changeProgramButton.Visibility = Visibility.Visible;
    }

    private static WpfImage ProgramIcon(BitmapSource icon) => new()
    {
        Source = icon,
        Width = 18,
        Height = 18,
        Stretch = Stretch.Uniform
    };

    private bool CanOpenCurrentFile => (_currentFilePath is not null || _shellOpenPath is not null) && !_isLaunchingProgram && !IsChoosingProgram;

    internal void OpenCurrentFile()
    {
        if (CanOpenCurrentFile && _shellOpenPath is not null) { OpenShortcut(); return; }
        if (!CanOpenCurrentFile || _currentFilePath is not { } filePath) return;
        var extension = Path.GetExtension(filePath);
        var selection = FileOpener.LoadSelection(extension, _settingsPath);
        if (selection is null) ShowProgramChoices(_openButton);
        else LaunchProgram(selection);
    }

    private async void ShowProgramChoices(WpfButton anchor)
    {
        if (!CanOpenCurrentFile || _currentFilePath is null) return;
        var filePath = _currentFilePath;
        var request = _request;
        var extension = Path.GetExtension(filePath);
        var menu = new WpfContextMenu
        {
            PlacementTarget = anchor,
            Style = (Style)FindResource("ProgramPickerMenu")
        };
        ProgramMenu = menu;
        menu.Items.Add(new WpfMenuItem { Header = "Finding available programs…", IsEnabled = false });
        var browseItem = new WpfMenuItem { Header = "Choose another program…" };
        AddProgramActions(menu, browseItem, remember => BrowseForProgram(filePath, remember));
        menu.Items.Add(browseItem);
        menu.IsOpen = true;

        var handlers = await ShellFileHandlers.ListAsync(extension);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (request == _request) PopulateProgramMenu(menu, browseItem, filePath, extension, handlers);
        }));
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
            AddProgramActions(menu, item, remember =>
                ChooseProgram(filePath, new FileOpener.Selection(null, handler.Name, handler.DisplayName), remember));
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
            AddProgramActions(menu, paintItem, remember => ChooseExecutable(filePath, paint, remember));
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

    private void AddProgramActions(WpfContextMenu menu, WpfMenuItem program, Action<bool> choose)
    {
        var request = _request;
        foreach (var remember in new[] { false, true })
        {
            var action = new WpfMenuItem { Header = remember ? "Open and remember" : "Open once" };
            action.Click += (_, e) =>
            {
                e.Handled = true;
                menu.IsOpen = false;
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (request == _request && CanOpenCurrentFile) choose(remember);
                });
            };
            program.Items.Add(action);
        }
    }

    private void BrowseForProgram(string filePath, bool remember)
    {
        _programDialogOpen = true;
        try
        {
            var dialog = new EditorFileDialog
            {
                Title = remember ? "Choose a program to open and remember" : "Choose a program to open once",
                Filter = "Programs (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true) ChooseExecutable(filePath, dialog.FileName, remember);
        }
        finally { _programDialogOpen = false; }
    }

    private void ChooseExecutable(string filePath, string executablePath, bool remember)
    {
        try
        {
            ChooseProgram(filePath, FileOpener.ExecutableSelection(executablePath), remember);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not open program");
        }
    }

    private void ChooseProgram(string filePath, FileOpener.Selection selection, bool remember)
    {
        if (_currentFilePath != filePath) return;
        try
        {
            if (remember) FileOpener.SaveSelection(Path.GetExtension(filePath), selection, _settingsPath);
            LaunchProgram(selection);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Could not save program choice");
        }
    }

    private void LaunchProgram(FileOpener.Selection selection)
    {
        if (_isLaunchingProgram || _currentFilePath is not { } filePath) return;
        var request = ++_launchRequest;
        SetProgramLaunching(true);
        Task launch = ProgramLauncher.OpenAsync(selection, filePath);
        _ = launch.ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            var error = task.Exception?.GetBaseException();
            if (request != _launchRequest || _currentFilePath != filePath || !IsVisible) return;
            SetProgramLaunching(false);
            if (error is null) { Hide(); return; }
            RefreshPreferredApp();
            if (error is FileNotFoundException && selection.HandlerName is not null)
                ShowProgramChoices(_changeProgramButton);
            else
                System.Windows.MessageBox.Show(this, error.Message, "Could not open file");
        })));
    }

    private void SetProgramLaunching(bool launching)
    {
        _isLaunchingProgram = launching;
        _openLabel.Visibility = launching ? Visibility.Hidden : Visibility.Visible;
        _openingLabel.Visibility = launching ? Visibility.Visible : Visibility.Hidden;
        System.Windows.Automation.AutomationProperties.SetItemStatus(_openButton, launching ? "Opening" : "");
        if (launching)
        {
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(_openButton) ??
                       System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(_openButton);
            peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }

    private void ShowJson(string path)
    {
        ShowMessage("Loading JSON preview…");
        _ = LoadPreviewAsync(() => JsonPreviewReader.Read(path), data =>
        {
            _body.Children.Clear();
            _jsonView = new JsonPreviewView(path, data);
            _body.Children.Add(_jsonView);
        }, "Could not read this JSON file. File details are shown above.");
    }

    private async Task LoadPreviewAsync<T>(Func<T> read, Action<T> show, string failureMessage, bool sta = false)
    {
        var current = _request;
        using var cancellation = new CancellationTokenSource();
        _previewLoad = cancellation;
        try
        {
            var result = await PreviewWork.RunAsync(read, cancellation.Token, sta).ConfigureAwait(false);
            if (Dispatcher.HasShutdownStarted)
            {
                if (result is IDisposable disposable) disposable.Dispose();
                return;
            }
            await Dispatcher.InvokeAsync(() =>
            {
                if (current != _request || !IsVisible)
                {
                    if (result is IDisposable disposable) disposable.Dispose();
                    return;
                }
                show(result);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (current == _request && IsVisible) ShowMessage(failureMessage);
                });
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_previewLoad, cancellation)) _previewLoad = null;
                });
        }
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
            Margin = new Thickness(0, 5, 18, 10)
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
        if (_shellPreview is not null) { _nativeHost = host; return true; }
        _body.Children.Clear();
        host.Dispose();
        return false;
    }

    private void ShowDocumentText(string path)
    {
        ShowMessage("Loading document text…");
        _ = LoadPreviewAsync(() => DocumentPreview.Read(path), ShowText,
            "Could not read this document. File details are shown above.");
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
        ClearNavigation();
        ResetContent();
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
        Hide();
        _allowClose = true;
        Close();
    }

    private void DisposeNativePreview()
    {
        _shellPreview?.Dispose();
        _shellPreview = null;
        _nativeHost?.Dispose();
        _nativeHost = null;
    }
}
