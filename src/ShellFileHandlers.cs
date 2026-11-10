using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace BetterQuickLook;

internal static class ShellFileHandlers
{
    internal sealed record Handler(string Name, string DisplayName, bool Recommended, BitmapSource? Icon = null);
    private static readonly Dictionary<string, Task<IReadOnlyList<Handler>>> CachedLists = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Extension, string Name, string Label), Task<BitmapSource?>> PreferredIcons = new();

    internal static Task<IReadOnlyList<Handler>> ListAsync(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return Task.FromResult<IReadOnlyList<Handler>>([]);
        lock (CachedLists)
        {
            if (CachedLists.TryGetValue(extension, out var cached)) return cached;
            var completion = new TaskCompletionSource<IReadOnlyList<Handler>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                try { completion.SetResult(List(extension)); }
                catch
                {
                    lock (CachedLists) CachedLists.Remove(extension);
                    completion.SetResult([]);
                }
            }) { IsBackground = true, Name = "BetterQuickLook Open With" };
            worker.SetApartmentState(ApartmentState.STA);
            CachedLists[extension] = completion.Task;
            worker.Start();
            return completion.Task;
        }
    }

    internal static IReadOnlyList<Handler> List(string extension)
    {
        var result = new List<Handler>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in Enumerate(extension))
        {
            try
            {
                var name = ReadString(handler.GetName);
                var displayName = ReadString(handler.GetUIName);
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(displayName) && seen.Add(name))
                {
                    result.Add(new Handler(name, displayName, handler.IsRecommended() == 0, ReadIcon(handler, name)));
                }
            }
            finally { Marshal.ReleaseComObject(handler); }
        }
        return result.OrderByDescending(handler => handler.Recommended)
            .ThenBy(handler => handler.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static bool Matches(Handler handler, string name, string displayName) =>
        string.Equals(handler.Name, name, StringComparison.OrdinalIgnoreCase) ||
        (Path.IsPathFullyQualified(handler.Name) && Path.IsPathFullyQualified(name) &&
         string.Equals(Path.GetFileName(handler.Name), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase) &&
         string.Equals(handler.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

    internal static async Task<BitmapSource?> PreferredIconAsync(string extension, string name, string displayName)
    {
        Task<IReadOnlyList<Handler>>? list;
        lock (CachedLists) CachedLists.TryGetValue(extension, out list);
        if (list is not null)
        {
            var handlers = await list.ConfigureAwait(false);
            var icon = handlers.FirstOrDefault(handler => Matches(handler, name, displayName))?.Icon;
            return icon ?? await ProgramIcons.ExtractAsync(name).ConfigureAwait(false);
        }

        Task<BitmapSource?> preferred;
        lock (PreferredIcons)
        {
            var key = (extension, name, displayName);
            if (!PreferredIcons.TryGetValue(key, out preferred!))
                PreferredIcons[key] = preferred = RunOnSta(() => FindPreferredIcon(extension, name, displayName), "BetterQuickLook Preferred Icon");
        }
        return await preferred.ConfigureAwait(false);
    }

    private static BitmapSource? FindPreferredIcon(string extension, string name, string displayName)
    {
        try
        {
            if (extension.Length > 0)
                foreach (var handler in Enumerate(extension))
                {
                    try
                    {
                        var candidate = ReadString(handler.GetName);
                        var label = ReadString(handler.GetUIName);
                        if (candidate is not null && label is not null && Matches(new Handler(candidate, label, false), name, displayName))
                            return ReadIcon(handler, candidate);
                    }
                    finally { Marshal.ReleaseComObject(handler); }
                }
        }
        catch (COMException) { }
        return ProgramIcons.Extract(name);
    }

    internal static Task OpenAsync(string extension, string handlerName, string displayName, string filePath) =>
        RunOnSta(() => { Open(extension, handlerName, displayName, filePath); return true; }, "BetterQuickLook Open File");

    private static Task<T> RunOnSta<T>(Func<T> work, string threadName)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = threadName };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    internal static void Open(string extension, string handlerName, string displayName, string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("File was not found.", filePath);
        foreach (var handler in Enumerate(extension))
        {
            try
            {
                var name = ReadString(handler.GetName);
                var label = ReadString(handler.GetUIName);
                if (name is null || label is null || !Matches(new Handler(name, label, false), handlerName, displayName)) continue;
                var data = CreateShellDataObject(filePath);
                try { Marshal.ThrowExceptionForHR(handler.Invoke(data)); }
                finally { Marshal.Release(data); }
                return;
            }
            finally { Marshal.ReleaseComObject(handler); }
        }
        throw new FileNotFoundException("The selected program is no longer available for this file type.", handlerName);
    }

    private static IntPtr CreateShellDataObject(string filePath)
    {
        IntPtr folder = IntPtr.Zero, file = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.GetDirectoryName(filePath)!, IntPtr.Zero, out folder, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(filePath, IntPtr.Zero, out file, 0, out _));
            var iid = new Guid("0000010e-0000-0000-c000-000000000046");
            Marshal.ThrowExceptionForHR(SHCreateDataObject(folder, 1, [ILFindLastID(file)], IntPtr.Zero, ref iid, out data));
            return data;
        }
        finally
        {
            if (folder != IntPtr.Zero) Marshal.FreeCoTaskMem(folder);
            if (file != IntPtr.Zero) Marshal.FreeCoTaskMem(file);
        }
    }

    private static IEnumerable<IAssocHandler> Enumerate(string extension)
    {
        Marshal.ThrowExceptionForHR(SHAssocEnumHandlers(extension, 0, out var handlers));
        try
        {
            while (handlers.Next(1, out var handler, out var fetched) == 0 && fetched == 1)
                yield return handler;
        }
        finally { Marshal.ReleaseComObject(handlers); }
    }

    private delegate int GetHandlerString(out IntPtr value);

    private static BitmapSource? ReadIcon(IAssocHandler handler, string name)
    {
        var location = ReadIconLocation(handler);
        return ProgramIcons.Extract(location.Path, location.Index) ?? ProgramIcons.Extract(name);
    }

    private static (string? Path, int Index) ReadIconLocation(IAssocHandler handler)
    {
        if (handler.GetIconLocation(out var value, out var index) != 0) return (null, 0);
        try { return (Marshal.PtrToStringUni(value), index); }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    private static string? ReadString(GetHandlerString get)
    {
        if (get(out var value) != 0) return null;
        try { return Marshal.PtrToStringUni(value); }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHAssocEnumHandlers(string extension, int filter, out IEnumAssocHandlers handlers);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributes, out uint actualAttributes);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(IntPtr folder, uint count, [In] IntPtr[] items, IntPtr inner,
        ref Guid iid, out IntPtr data);

    [ComImport, Guid("973810ae-9599-4b88-9e4d-6ee98c9552da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumAssocHandlers
    {
        [PreserveSig] int Next(uint count, out IAssocHandler handler, out uint fetched);
    }

    [ComImport, Guid("f04061ac-1659-4a3f-a954-775aa57fc083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssocHandler
    {
        [PreserveSig] int GetName(out IntPtr name);
        [PreserveSig] int GetUIName(out IntPtr displayName);
        [PreserveSig] int GetIconLocation(out IntPtr path, out int index);
        [PreserveSig] int IsRecommended();
        [PreserveSig] int MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int Invoke(IntPtr data);
        [PreserveSig] int CreateInvoker(IntPtr data, out IntPtr invoker);
    }
}
