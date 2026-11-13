using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace BetterQuickLook;

public partial class App : System.Windows.Application
{
    internal static readonly Uri IconUri = new("pack://application:,,,/BetterQuickLook;component/Assets/BetterQuickLook.ico");

    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private WinForms.NotifyIcon? _tray;
    private KeyboardHook? _hook;
    private PreviewWindow? _preview;
    private SelectionFollower? _selectionFollower;
    private ShellSelectionEvents? _selectionEvents;
    private bool _opening;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\BetterQuickLook", out var isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }
        using var iconStream = GetResourceStream(IconUri).Stream;
        _tray = new WinForms.NotifyIcon
        {
            Icon = new Icon(iconStream, WinForms.SystemInformation.SmallIconSize),
            Text = "BetterQuickLook — Space on Desktop or in File Explorer",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.DoubleClick += (_, _) => WinForms.MessageBox.Show(
            "Select one file on the Desktop or in File Explorer and press Space. Press Space or Escape to close the preview.",
            "BetterQuickLook");

        _hook = new KeyboardHook(() => _preview?.IsVisible == true && !_preview.IsChoosingProgram && !_preview.IsProgramButtonFocused,
            () => Dispatcher.BeginInvoke(TogglePreview),
            () => Dispatcher.BeginInvoke(() => _preview?.OpenCurrentFile()));
    }

    private void TogglePreview()
    {
        if (_opening) return;
        if (_preview?.IsVisible == true)
        {
            _preview.Hide();
            return;
        }

        _opening = true;
        try
        {
            var path = ExplorerSelection.GetSelectedFile();
            if (path is null) return;
            if (_preview is null)
            {
                _preview = new PreviewWindow();
                _selectionFollower = new SelectionFollower(KeyboardHook.FocusedShellWindow,
                    ExplorerSelection.ReadSelectedFileAsync,
                    () => _preview.IsVisible && !_preview.IsChoosingProgram,
                    selected =>
                    {
                        if (selected is null) _preview.ShowSelectionPrompt();
                        else _preview.ShowFile(selected, activate: false);
                    });
                _preview.IsVisibleChanged += (_, _) =>
                {
                    if (_preview.IsVisible) return;
                    _selectionEvents?.Dispose();
                    _selectionEvents = null;
                    _selectionFollower.Stop();
                };
            }
            _preview.ShowFile(path, activate: false);
            _selectionEvents ??= new ShellSelectionEvents(_selectionFollower!.RequestRefresh);
            _selectionFollower!.Start(path);
        }
        finally { _opening = false; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _selectionEvents?.Dispose();
        _selectionFollower?.Dispose();
        _preview?.ClosePermanently();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        if (_tray is not null)
        {
            var icon = _tray.Icon;
            _tray.Visible = false;
            _tray.Dispose();
            icon?.Dispose();
        }
        base.OnExit(e);
    }
}
