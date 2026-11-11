using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace BetterQuickLook;

internal enum ShortcutStatus { File, Folder, Missing, Network, Virtual, Invalid, Cycle, TooDeep }
internal sealed record ShortcutInfo(string ShortcutPath, string? Target, string Description, string Arguments,
    string WorkingDirectory, ShortcutStatus Status, int Links);

internal static class ShortcutPreview
{
    internal const int MaxLinks = 8;

    // Read saved metadata only. Resolve() can search disks, contact remote hosts,
    // show dialogs, and update the link; none of that belongs in a preview.
    internal sealed record SavedLink(string Target, string Description, string Arguments, string Directory);

    internal static ShortcutInfo Read(string path) => Read(path, ReadSavedLink);

    internal static ShortcutInfo Read(string path, Func<string, SavedLink> readLink)
    {
        var original = Path.GetFullPath(path);
        var current = original;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string description = "", arguments = "", directory = "";
        for (var depth = 0; depth < MaxLinks; depth++)
        {
            if (!seen.Add(current)) return Result(current, ShortcutStatus.Cycle, depth);
            if (PreviewPath.IsNetwork(current)) return Result(current, ShortcutStatus.Network, depth);
            if (!File.Exists(current)) return Result(current, ShortcutStatus.Missing, depth);
            try
            {
                var saved = readLink(current);
                var target = Environment.ExpandEnvironmentVariables(saved.Target);
                if (depth == 0)
                { description = saved.Description; arguments = saved.Arguments; directory = saved.Directory; }
                if (string.IsNullOrWhiteSpace(target)) return Result(null, ShortcutStatus.Virtual, depth + 1);
                current = Path.GetFullPath(target, Path.GetDirectoryName(current)!);
                if (PreviewPath.IsNetwork(current)) return Result(current, ShortcutStatus.Network, depth + 1);
                if (Path.GetExtension(current).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                var status = Directory.Exists(current) ? ShortcutStatus.Folder : File.Exists(current) ? ShortcutStatus.File : ShortcutStatus.Missing;
                return Result(current, status, depth + 1);
            }
            catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { return Result(current == original ? null : current, ShortcutStatus.Invalid, depth + 1); }
        }
        return Result(current, ShortcutStatus.TooDeep, MaxLinks);

        ShortcutInfo Result(string? target, ShortcutStatus status, int links) => new(original, target, description, arguments, directory, status, links);
    }

    private static SavedLink ReadSavedLink(string path)
    {
        object instance = new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0x40); // Read-only, share-deny-none.
            var link = (IShellLinkW)instance;
            var text = new StringBuilder(32768);
            link.GetPath(text, text.Capacity, IntPtr.Zero, 4);
            var target = text.ToString();
            text.Clear(); link.GetDescription(text, text.Capacity); var description = text.ToString();
            text.Clear(); link.GetArguments(text, text.Capacity); var arguments = text.ToString();
            text.Clear(); link.GetWorkingDirectory(text, text.Capacity);
            return new(target, description, arguments, text.ToString());
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr data, uint flags);
        void GetIDList(out IntPtr item);
        void SetIDList(IntPtr item);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
    }
}
