using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BetterQuickLook;

// Installed only while the preview is visible. OUTOFCONTEXT delivers callbacks
// on this UI thread, so callbacks only schedule work and never call Shell COM.
internal sealed class ShellSelectionEvents : IDisposable
{
    private readonly WinEventProc _callback;
    private readonly Action _changed;
    private readonly List<IntPtr> _hooks = [];

    internal ShellSelectionEvents(Action changed)
    {
        _changed = changed;
        _callback = OnEvent;
        try
        {
            Hook(0x0003, 0x0003); // Foreground window, including a different Explorer.
            Hook(0x8005, 0x8009); // Focus and all selection-change variants.
            Hook(0x800C, 0x800C); // Explorer title/tab/folder name changes.
        }
        catch { Dispose(); throw; }
    }

    private void Hook(uint first, uint last)
    {
        var hook = SetWinEventHook(first, last, IntPtr.Zero, _callback, 0, 0, 2);
        if (hook == IntPtr.Zero) throw new Win32Exception();
        _hooks.Add(hook);
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint threadId, uint time)
    {
        if (eventType == 0x0003) { _changed(); return; }
        var source = KeyboardHook.FocusedShellWindow();
        if (source != IntPtr.Zero && (window == source || GetAncestor(window, 2) == source))
            _changed();
    }

    public void Dispose()
    {
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
