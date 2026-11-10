using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BetterQuickLook;

internal static class ProgramIcons
{
    private static BitmapSource? _fallback;
    private static readonly Dictionary<(string Location, int Index), Task<BitmapSource?>> CachedIcons = new();

    internal static BitmapSource Fallback => _fallback ??= FreezeIcon(System.Drawing.SystemIcons.Application.Handle);

    internal static Task<BitmapSource?> ExtractAsync(string? location, int index = 0)
    {
        if (string.IsNullOrWhiteSpace(location)) return Task.FromResult<BitmapSource?>(null);
        lock (CachedIcons)
        {
            if (CachedIcons.TryGetValue((location, index), out var cached)) return cached;
            var completion = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() => completion.SetResult(Extract(location, index)))
            { IsBackground = true, Name = "BetterQuickLook Program Icon" };
            worker.SetApartmentState(ApartmentState.STA);
            CachedIcons[(location, index)] = completion.Task;
            worker.Start();
            return completion.Task;
        }
    }

    internal static BitmapSource? Extract(string? location, int index = 0)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var path = Environment.ExpandEnvironmentVariables(location.Trim('"'));
        if (!File.Exists(path)) return null;

        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            if (SHDefExtractIconW(path, index, 0, out large, out small, (20u << 16) | 32u) < 0)
                return null;
            var icon = small != IntPtr.Zero ? small : large;
            return icon == IntPtr.Zero ? null : FreezeIcon(icon);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (small != IntPtr.Zero) DestroyIcon(small);
            if (large != IntPtr.Zero) DestroyIcon(large);
        }
    }

    private static BitmapSource FreezeIcon(IntPtr icon)
    {
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHDefExtractIconW(string path, int index, uint flags,
        out IntPtr largeIcon, out IntPtr smallIcon, uint iconSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
