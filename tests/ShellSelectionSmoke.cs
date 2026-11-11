using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using BetterQuickLook;

internal static partial class Program
{
    private static void DesktopSelectionSmoke()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var preview = new PreviewWindow(Path.Combine(_folder, "preferences.json"));
        var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var prefix = "BetterQuickLook-selection-test-" + Guid.NewGuid().ToString("N");
        var first = Path.Combine(desktopFolder, prefix + "-a.png");
        var second = Path.Combine(desktopFolder, prefix + "-b.json");
        var previousForeground = SelectionGetForegroundWindow();
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        object windows = ((dynamic)shell).Windows();
        object location = 0;
        object? root = null;
        int desktopHandle = 0;
        object desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, ref desktopHandle, 1);
        object document = ((dynamic)desktop).Document;
        object folder = ((dynamic)document).Folder;
        var previousSelection = new List<string>();
        object selected = ((dynamic)document).SelectedItems();
        for (var i = 0; i < (int)((dynamic)selected).Count; i++)
        {
            object item = ((dynamic)selected).Item(i);
            try { previousSelection.Add((string)((dynamic)item).Path); }
            finally { Marshal.ReleaseComObject(item); }
        }
        Marshal.ReleaseComObject(selected);
        SelectionFollower? follower = null;
        ShellSelectionEvents? events = null;
        var minimized = false;
        try
        {
            using (var bitmap = new Bitmap(40, 30)) bitmap.Save(first, ImageFormat.Png);
            File.WriteAllText(second, "{\"desktop\":true}");
            ToggleDesktopKeys();
            minimized = true;
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            SelectionSetForegroundWindow(new IntPtr(desktopHandle));
            Until(() => Select(first, 1 | 4 | 8 | 16), "Desktop fixture appears");
            Until(() => KeyboardHook.FocusedShellWindow() != IntPtr.Zero, "Desktop focus");
            preview.ShowFile(first, activate: false);
            follower = new SelectionFollower(KeyboardHook.FocusedShellWindow, ExplorerSelection.ReadSelectedFileAsync,
                () => preview.IsVisible, path =>
                { if (path is null) preview.ShowSelectionPrompt(); else preview.ShowFile(path, activate: false); });
            events = new ShellSelectionEvents(follower.RequestRefresh);
            follower.Start(first);
            Until(() => Select(second, 1 | 4 | 8 | 16), "second Desktop fixture appears");
            Until(() => ContainsText(preview, Path.GetFileName(second)), "Desktop selection updates preview");
            Select(first, 1 | 4 | 8 | 16);
            Until(() => ContainsText(preview, Path.GetFileName(first)), "Desktop image selection updates preview");
            Select(second, 1 | 8); // Add to selection without clearing the first.
            Until(() => ContainsText(preview, "No single item selected"), "Desktop multiple selection clears preview");
        }
        finally
        {
            events?.Dispose();
            follower?.Dispose();
            preview.ClosePermanently();
            Select(first, 4); // Deselect all, then restore the user's previous selection.
            foreach (var path in previousSelection) Select(path, 1);
            File.Delete(first);
            File.Delete(second);
            foreach (var value in new[] { folder, document, desktop, windows }) Marshal.ReleaseComObject(value);
            if (minimized) ToggleDesktopKeys();
            Marshal.ReleaseComObject(shell);
            SelectionSetForegroundWindow(previousForeground);
            app.Shutdown();
        }

        bool Select(string path, int flags)
        {
            object? item = ((dynamic)folder).ParseName(Path.GetFileName(path));
            if (item is null) return false;
            try { ((dynamic)document).SelectItem(item, flags); return true; }
            finally { Marshal.ReleaseComObject(item); }
        }
    }

    private static void ToggleDesktopKeys()
    {
        SelectionKeyEvent(0x5B, 0, 0, UIntPtr.Zero);
        SelectionKeyEvent(0x44, 0, 0, UIntPtr.Zero);
        SelectionKeyEvent(0x44, 0, 2, UIntPtr.Zero);
        SelectionKeyEvent(0x5B, 0, 2, UIntPtr.Zero);
    }

    private static void ShellSelectionSmoke()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var preview = new PreviewWindow(Path.Combine(_folder, "preferences.json"));
        var sourceFolder = Directory.CreateDirectory(Path.Combine(_folder, "selection-source")).FullName;
        var first = Path.Combine(sourceFolder, "a-image.png");
        var second = Path.Combine(sourceFolder, "b-data.json");
        using (var bitmap = new Bitmap(40, 30)) bitmap.Save(first, ImageFormat.Png);
        File.WriteAllText(second, "{\"selection\":\"second file\"}");
        var originalForeground = SelectionGetForegroundWindow();
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        object windows = ((dynamic)shell).Windows();
        var existing = new HashSet<long>();
        foreach (var item in (dynamic)windows)
        {
            try { existing.Add((long)((dynamic)item).HWND); }
            finally { Marshal.ReleaseComObject(item); }
        }
        object? explorer = null, document = null, folder = null;
        SelectionFollower? follower = null;
        ShellSelectionEvents? events = null;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.Arguments = "/n,\"" + sourceFolder + "\"";
            using var process = Process.Start(start);
            Until(() =>
            {
                foreach (var item in (dynamic)windows)
                {
                    var keep = false;
                    try
                    {
                        var url = (string)((dynamic)item).LocationURL;
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile &&
                            uri.LocalPath.TrimEnd('\\').Equals(sourceFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            Check(!existing.Contains((long)((dynamic)item).HWND), "test must own a new Explorer window");
                            explorer = item;
                            keep = true;
                            return true;
                        }
                    }
                    finally { if (!keep) Marshal.ReleaseComObject(item); }
                }
                return false;
            }, "open fixture Explorer window");
            var handle = new IntPtr((long)((dynamic)explorer!).HWND);
            document = ((dynamic)explorer!).Document;
            folder = ((dynamic)document).Folder;
            ((dynamic)document).CurrentViewMode = 4; // Details: up/down navigate rows.
            // Establish input ownership before asking Windows to foreground the
            // fixture. A background test runner otherwise cannot always activate it.
            SelectionKeyEvent(0x12, 0, 0, UIntPtr.Zero);
            SelectionKeyEvent(0x12, 0, 2, UIntPtr.Zero);
            SelectionSetForegroundWindow(handle);
            Select("a-image.png");
            Until(() => KeyboardHook.FocusedShellWindow() == handle, "Explorer list focus");
            preview.ShowFile(first, activate: false);
            Check(SelectionGetForegroundWindow() == handle, "opening preview keeps Explorer focus");
            follower = new SelectionFollower(KeyboardHook.FocusedShellWindow, ExplorerSelection.ReadSelectedFileAsync,
                () => preview.IsVisible && !preview.IsChoosingProgram,
                path => { if (path is null) preview.ShowSelectionPrompt(); else preview.ShowFile(path, activate: false); });
            events = new ShellSelectionEvents(follower.RequestRefresh);
            follower.Start(first);
            var left = preview.Left;
            var top = preview.Top;

            Select("b-data.json");
            Until(() => ContainsText(preview, "b-data.json"), "selection event updates JSON preview");
            Check(preview.Left == left && preview.Top == top && SelectionGetForegroundWindow() == handle,
                "selection update preserves position and Explorer focus");
            Select("a-image.png");
            Until(() => ContainsText(preview, "a-image.png"), "selection event updates image preview");
            var beforeArrow = ExplorerSelection.GetSelectedFile(handle);
            SelectionKeyEvent(0x28, 0, 0, UIntPtr.Zero);
            SelectionKeyEvent(0x28, 0, 2, UIntPtr.Zero);
            Until(() => ExplorerSelection.GetSelectedFile(handle) is { } selected && selected != beforeArrow,
                "Down arrow changes Explorer selection");
            var afterArrow = ExplorerSelection.GetSelectedFile(handle)!;
            Until(() => ContainsText(preview, Path.GetFileName(afterArrow)), "arrow selection updates preview");

            var openRequests = 0;
            using (var keyboard = new KeyboardHook(() => preview.IsVisible, () => { }, () => openRequests++))
            {
                SelectionKeyEvent(0x11, 0, 0, UIntPtr.Zero);
                SelectionKeyEvent(0x0D, 0, 0, UIntPtr.Zero);
                SelectionKeyEvent(0x0D, 0, 0, UIntPtr.Zero); // Repeated keydown must not open twice.
                SelectionKeyEvent(0x0D, 0, 2, UIntPtr.Zero);
                SelectionKeyEvent(0x11, 0, 2, UIntPtr.Zero);
                Until(() => openRequests > 0, "Ctrl+Enter while Explorer retains focus");
                Check(openRequests == 1, "held Ctrl+Enter requests one launch");
            }

            object all = ((dynamic)folder).Items();
            object firstItem = ((dynamic)all).Item(0);
            try { ((dynamic)document).SelectItem(firstItem, 4); }
            finally { Marshal.ReleaseComObject(firstItem); Marshal.ReleaseComObject(all); }
            Until(() => ContainsText(preview, "No single item selected"), "deselection clears preview");
            Select("a-image.png");
            Until(() => ContainsText(preview, "a-image.png"), "preview recovers after deselection");
            follower.Stop();
            events.Dispose();
            preview.Hide();
            Select("b-data.json");
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            Check(!preview.IsVisible, "selection after close cannot reopen preview");
        }
        finally
        {
            events?.Dispose();
            follower?.Dispose();
            preview.ClosePermanently();
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (document is not null) Marshal.ReleaseComObject(document);
            if (explorer is not null)
            {
                try { ((dynamic)explorer).Quit(); }
                finally { Marshal.ReleaseComObject(explorer); }
            }
            Marshal.ReleaseComObject(windows);
            Marshal.ReleaseComObject(shell);
            SelectionSetForegroundWindow(originalForeground);
            app.Shutdown();
        }

        void Select(string name)
        {
            object item = ((dynamic)folder!).ParseName(name);
            try { ((dynamic)document!).SelectItem(item, 1 | 4 | 8 | 16); }
            finally { Marshal.ReleaseComObject(item); }
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr SelectionGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool SelectionSetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void SelectionKeyEvent(byte key, byte scan, uint flags, UIntPtr extra);
}
