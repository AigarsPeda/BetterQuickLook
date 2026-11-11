using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterQuickLook;

internal static class ExplorerSelection
{
    public static string? GetSelectedFile()
    {
        var foreground = GetForegroundWindow();
        var isDesktop = ClassName(foreground) is "Progman" or "WorkerW";
        var title = WindowTitle(foreground);
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;
        object? shell = null;
        object? windows = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            windows = ((dynamic)shell).Windows();
            if (isDesktop) return GetDesktopSelection(windows);
            var candidates = new List<(string Name, string? Path)>();
            foreach (var window in (dynamic)windows)
            {
                object? document = null, selected = null, item = null;
                try
                {
                    if ((long)((dynamic)window).HWND != foreground.ToInt64()) continue;
                    document = ((dynamic)window).Document;
                    selected = ((dynamic)document).SelectedItems();
                    string locationName = (string)((dynamic)window).LocationName;
                    if ((int)((dynamic)selected).Count != 1)
                    {
                        candidates.Add((locationName, null));
                        continue;
                    }
                    item = ((dynamic)selected).Item(0);
                    string path = (string)((dynamic)item).Path;
                    candidates.Add((locationName, File.Exists(path) || Directory.Exists(path) ? path : null));
                }
                catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException) { }
                finally
                {
                    Release(item);
                    Release(selected);
                    Release(document);
                    Release(window);
                }
            }
            return ChooseSelection(title, candidates);
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException) { }
        finally
        {
            if (windows is not null && Marshal.IsComObject(windows)) Marshal.ReleaseComObject(windows);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
        return null;
    }

    internal static string? GetDesktopSelection(object windows)
    {
        object location = 0;
        object? root = null;
        var desktopHandle = 0;
        object? desktop = null;
        object? document = null, selected = null, item = null;
        try
        {
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, ref desktopHandle, 1);
            if (desktop is null) return null;
            document = ((dynamic)desktop).Document;
            selected = ((dynamic)document).SelectedItems();
            if ((int)((dynamic)selected).Count != 1) return null;
            item = ((dynamic)selected).Item(0);
            string path = (string)((dynamic)item).Path;
            return File.Exists(path) || Directory.Exists(path) ? path : null;
        }
        finally
        {
            Release(item);
            Release(selected);
            Release(document);
            Release(desktop);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    internal static string? ChooseSelection(string title, IReadOnlyList<(string Name, string? Path)> candidates)
    {
        var active = candidates
            .Where(candidate => IsActiveTab(title, candidate.Name))
            .OrderByDescending(candidate => candidate.Name.Length)
            .ToList();
        if (active.Count > 0) return active[0].Path;
        return candidates.Count == 1 ? candidates[0].Path : null;
    }

    private static bool IsActiveTab(string windowTitle, string locationName) =>
        windowTitle.StartsWith(locationName + " ", StringComparison.OrdinalIgnoreCase) ||
        windowTitle.Equals(locationName, StringComparison.OrdinalIgnoreCase);

    private static string WindowTitle(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static string ClassName(IntPtr window)
    {
        var text = new StringBuilder(256);
        return GetClassName(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
}
