using System.Runtime.InteropServices;
using System.Text;

namespace BetterQuickLook;

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x100;
    private const int WmKeyUp = 0x101;
    private const int VkSpace = 0x20;
    private const int VkEscape = 0x1B;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkShift = 0x10;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;
    private readonly Func<bool> _previewVisible;
    private readonly Action _toggle;
    private readonly HookProc _callback;
    private IntPtr _handle;
    private bool _swallowKeyUp;
    private bool _swallowEscapeKeyUp;

    public KeyboardHook(Func<bool> previewVisible, Action toggle)
    {
        _previewVisible = previewVisible;
        _toggle = toggle;
        _callback = OnKey;
        _handle = SetWindowsHookEx(WhKeyboardLl, _callback, IntPtr.Zero, 0);
        if (_handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(_handle, code, message, data);
        var key = Marshal.ReadInt32(data);
        var msg = message.ToInt32();
        if (key == VkEscape && _swallowEscapeKeyUp && msg == WmKeyUp)
        {
            _swallowEscapeKeyUp = false;
            return new IntPtr(1);
        }
        if (key == VkEscape && _swallowEscapeKeyUp && msg == WmKeyDown)
            return new IntPtr(1);
        if (key == VkEscape && msg == WmKeyDown && _previewVisible())
        {
            _swallowEscapeKeyUp = true;
            _toggle();
            return new IntPtr(1);
        }
        if (key == VkSpace && _swallowKeyUp && msg == WmKeyUp)
        {
            _swallowKeyUp = false;
            return new IntPtr(1);
        }
        if (key == VkSpace && _swallowKeyUp && msg == WmKeyDown)
            return new IntPtr(1);
        if (key != VkSpace || msg != WmKeyDown)
            return CallNextHookEx(_handle, code, message, data);
        if (Down(VkControl) || Down(VkAlt) || Down(VkShift) || Down(VkLwin) || Down(VkRwin))
            return CallNextHookEx(_handle, code, message, data);
        if (!_previewVisible() && !ShellFileListHasFocus())
            return CallNextHookEx(_handle, code, message, data);

        _swallowKeyUp = true;
        _toggle();
        return new IntPtr(1);
    }

    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static bool ShellFileListHasFocus()
    {
        var foreground = GetForegroundWindow();
        var name = ClassName(foreground);
        if (name is "Progman" or "WorkerW") return AcceptsShellSpace(name, "");
        if (name is not ("CabinetWClass" or "ExploreWClass")) return false;
        var threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(threadId, ref info)) return false;
        return AcceptsShellSpace(name, ClassName(info.hwndFocus));
    }

    internal static bool AcceptsShellSpace(string foregroundClass, string focusedClass) =>
        (foregroundClass is "Progman" or "WorkerW") ||
        (foregroundClass is "CabinetWClass" or "ExploreWClass") &&
        (focusedClass is "DirectUIHWND" or "SysListView32");

    private static string ClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public Rect rcCaret;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
}
