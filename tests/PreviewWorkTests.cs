using BetterQuickLook;

internal static partial class Program
{
    private sealed class DisposablePreview : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private static void PreviewWorkLimits()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var activeCancellation = new CancellationTokenSource();
        var discarded = new DisposablePreview();
        var ran = 0;
        var active = PreviewWork.RunAsync(() =>
        {
            Interlocked.Increment(ref ran);
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return discarded;
        }, activeCancellation.Token);
        var pending = new List<Task<int>>();
        Task<int>? latest = null;
        try
        {
            Check(started.Wait(TimeSpan.FromSeconds(5)), "first preview starts");
            for (var i = 0; i < 32; i++)
            {
                using var canceled = new CancellationTokenSource();
                pending.Add(PreviewWork.RunAsync(() => Interlocked.Increment(ref ran), canceled.Token));
                canceled.Cancel();
            }
            latest = PreviewWork.RunAsync(() => Interlocked.Increment(ref ran), CancellationToken.None);
            Check(!latest.IsCompleted && ran == 1, "only one expensive decoder may run at once");
            activeCancellation.Cancel();
        }
        finally { release.Set(); }
        ExpectFailure(() => active.GetAwaiter().GetResult(), "canceled active result is discarded");
        foreach (var task in pending)
        {
            ExpectFailure(() => task.GetAwaiter().GetResult(), "queued preview is canceled");
            Check(task.IsCanceled, "cancellation is reported without decoding");
        }
        Check(latest!.GetAwaiter().GetResult() == 2 && discarded.Disposed,
            "only first and final preview run; discarded resources are disposed");
        ExpectFailure(() => PreviewWork.RunAsync<int>(() => throw new InvalidOperationException("test"), CancellationToken.None)
            .GetAwaiter().GetResult(), "reader failures propagate");
        Check(PreviewWork.RunAsync(() => 42, CancellationToken.None).GetAwaiter().GetResult() == 42,
            "decoder failure releases the slot for the next preview");
    }
}
