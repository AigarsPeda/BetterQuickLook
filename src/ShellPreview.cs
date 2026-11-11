using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using WinForms = System.Windows.Forms;

namespace BetterQuickLook;

internal sealed class ShellPreview : IDisposable
{
    private const string PreviewHandlerId = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    private readonly IPreviewHandler _handler;
    private readonly object _comObject;
    private readonly WinForms.Panel _panel;
    private IStream? _stream;

    private ShellPreview(object comObject, IPreviewHandler handler, WinForms.Panel panel, IStream? stream)
    {
        _comObject = comObject;
        _handler = handler;
        _panel = panel;
        _stream = stream;
        _panel.Resize += OnResize;
    }

    public static ShellPreview? TryOpen(string path, IntPtr parent)
    {
        var id = GetHandlerId(Path.GetExtension(path));
        if (id is null) return null;
        var type = Type.GetTypeFromCLSID(id.Value) ?? throw new COMException("Preview handler is not registered.");
        var instance = Activator.CreateInstance(type) ?? throw new COMException("Could not start preview handler.");
        IStream? stream = null;
        ShellPreview? preview = null;
        try
        {
            if (instance is IInitializeWithStream initializeStream)
            {
                SHCreateStreamOnFileEx(path, 0, 0, false, null, out stream);
                initializeStream.Initialize(stream, 0);
            }
            else if (instance is IInitializeWithFile initializeFile)
                initializeFile.Initialize(path, 0);
            else
                throw new COMException("Handler does not support file or stream initialization.");

            if (instance is not IPreviewHandler handler) throw new COMException("Handler cannot display a preview.");
            var panel = WinForms.Control.FromHandle(parent) as WinForms.Panel;
            if (panel is null) throw new InvalidOperationException("Preview host is unavailable.");
            preview = new ShellPreview(instance, handler, panel, stream);
            var rectangle = ClientRectangle(parent);
            handler.SetWindow(parent, ref rectangle);
            handler.DoPreview();
            return preview;
        }
        catch
        {
            if (preview is not null) preview.Dispose();
            else
            {
                if (stream is not null) Marshal.ReleaseComObject(stream);
                Marshal.ReleaseComObject(instance);
            }
            throw;
        }
    }

    private static Guid? GetHandlerId(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        var length = 128u;
        var buffer = new StringBuilder((int)length);
        var result = AssocQueryString(0, 16, extension, PreviewHandlerId, buffer, ref length);
        return result == 0 && Guid.TryParse(buffer.ToString(), out var id) ? id : null;
    }

    private void OnResize(object? sender, EventArgs e)
    {
        var rectangle = ClientRectangle(_panel.Handle);
        try { _handler.SetRect(ref rectangle); }
        catch (COMException) { }
    }

    private static Rect ClientRectangle(IntPtr window)
    {
        GetClientRect(window, out var rectangle);
        return rectangle;
    }

    public void Dispose()
    {
        _panel.Resize -= OnResize;
        try { _handler.Unload(); }
        catch (COMException) { }
        if (_stream is not null) Marshal.ReleaseComObject(_stream);
        _stream = null;
        Marshal.ReleaseComObject(_comObject);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("8895B1C6-B41F-4C1C-A562-0D564250836F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref Rect rect);
        void SetRect(ref Rect rect);
        void DoPreview();
        void Unload();
        void SetFocus();
        void QueryFocus(out IntPtr hwnd);
        [PreserveSig] int TranslateAccelerator(IntPtr message);
    }

    [ComImport, Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithStream { void Initialize(IStream stream, uint mode); }

    [ComImport, Guid("B7D14566-0509-4CCE-A71F-0A554233BD9B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint str, string assoc, string extra,
        StringBuilder output, ref uint length);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateStreamOnFileEx(string file, uint mode, uint attributes,
        [MarshalAs(UnmanagedType.Bool)] bool create, IStream? template, out IStream stream);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
}
