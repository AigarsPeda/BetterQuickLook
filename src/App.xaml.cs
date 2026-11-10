using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace BetterQuickLook;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private WinForms.NotifyIcon? _tray;
    private KeyboardHook? _hook;
    private PreviewWindow? _preview;
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
        _tray = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BetterQuickLook — Space on Desktop or in File Explorer",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.DoubleClick += (_, _) => WinForms.MessageBox.Show(
            "Select one file on the Desktop or in File Explorer and press Space. Press Space or Escape to close the preview.",
            "BetterQuickLook");

        _hook = new KeyboardHook(() => _preview?.IsVisible == true && !_preview.IsChoosingEditor && !_preview.IsEditorButtonFocused,
            () => Dispatcher.BeginInvoke(TogglePreview));
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
            _preview ??= new PreviewWindow();
            _preview.ShowFile(path);
        }
        finally { _opening = false; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _preview?.ClosePermanently();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
