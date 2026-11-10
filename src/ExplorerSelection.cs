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
                try
                {
                    if ((long)((dynamic)window).HWND != foreground.ToInt64()) continue;
                    dynamic document = ((dynamic)window).Document;
                    dynamic selected = document.SelectedItems();
                    string locationName = (string)((dynamic)window).LocationName;
                    if ((int)selected.Count != 1)
                    {
                        candidates.Add((locationName, null));
                        continue;
                    }
                    string path = (string)selected.Item(0).Path;
                    candidates.Add((locationName, File.Exists(path) || Directory.Exists(path) ? path : null));
                }
                catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException) { }
                finally { if (Marshal.IsComObject(window)) Marshal.ReleaseComObject(window); }
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
        try
        {
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, ref desktopHandle, 1);
            if (desktop is null) return null;
            dynamic selected = ((dynamic)desktop).Document.SelectedItems();
            if ((int)selected.Count != 1) return null;
            string path = (string)selected.Item(0).Path;
            return File.Exists(path) || Directory.Exists(path) ? path : null;
        }
        finally { if (desktop is not null && Marshal.IsComObject(desktop)) Marshal.ReleaseComObject(desktop); }
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
