using System.Diagnostics;
using System.Windows.Threading;

namespace BetterQuickLook;

// Coalesce Explorer events and allow only one Shell query in flight. Everything
// except the supplied read operation runs on the preview's dispatcher.
internal sealed class SelectionFollower : IDisposable
{
    private readonly Func<IntPtr> _source;
    private readonly Func<IntPtr, Task<string?>> _read;
    private readonly Func<bool> _canUpdate;
    private readonly Action<string?> _update;
    private readonly DispatcherTimer _timer;
    private bool _active, _reading, _dirty;
    private int _revision;
    private string? _lastPath;

    internal SelectionFollower(Func<IntPtr> source, Func<IntPtr, Task<string?>> read,
        Func<bool> canUpdate, Action<string?> update)
    {
        _source = source;
        _read = read;
        _canUpdate = canUpdate;
        _update = update;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _timer.Tick += Refresh;
    }

    internal void Start(string path)
    {
        Stop();
        _lastPath = path;
        _active = true;
        RequestRefresh();
    }

    internal void Stop()
    {
        _active = false;
        _dirty = false;
        _revision++;
        _timer.Stop();
        _lastPath = null;
    }

    internal void RequestRefresh()
    {
        if (!_active) return;
        _revision++;
        _dirty = true;
        if (!_reading && !_timer.IsEnabled) _timer.Start();
    }

    private async void Refresh(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (!_active || _reading || !_dirty) return;
        _dirty = false;
        var source = _source();
        if (source == IntPtr.Zero || !_canUpdate()) return;
        _reading = true;
        var revision = _revision;
        try
        {
            var path = await _read(source);
            if (!_active || revision != _revision || source != _source() || !_canUpdate()) return;
            if (string.Equals(path, _lastPath, StringComparison.OrdinalIgnoreCase)) return;
            _update(path);
            _lastPath = path;
        }
        catch (Exception error)
        {
            // Explorer may close or restart during a COM query. A later selection
            // event can retry; it must not close or reopen the preview.
            Debug.WriteLine($"Selection preview: {error.Message}");
        }
        finally
        {
            _reading = false;
            if (_active && _dirty) _timer.Start();
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= Refresh;
    }
}
