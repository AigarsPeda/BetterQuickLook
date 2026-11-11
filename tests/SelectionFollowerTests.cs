using BetterQuickLook;

internal static partial class Program
{
    private static void SelectionFollowing()
    {
        var source = new IntPtr(42);
        var allowed = true;
        var reads = new Queue<TaskCompletionSource<string?>>();
        var updates = new List<string?>();
        var queryCount = 0;
        using var follower = new SelectionFollower(() => source, _ =>
        {
            queryCount++;
            var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            reads.Enqueue(result);
            return result.Task;
        }, () => allowed, updates.Add);

        follower.Start("first.png");
        Until(() => reads.Count == 1, "initial selection read");
        reads.Dequeue().SetResult("FIRST.PNG");
        PumpDispatcher(TimeSpan.FromMilliseconds(50));
        Check(updates.Count == 0, "duplicate paths do not reload the preview");

        follower.RequestRefresh();
        Until(() => reads.Count == 1, "next selection read");
        for (var i = 0; i < 100; i++) follower.RequestRefresh();
        PumpDispatcher(TimeSpan.FromMilliseconds(100));
        Check(queryCount == 2, "rapid events do not start concurrent Shell queries");
        reads.Dequeue().SetResult("obsolete.png");
        Until(() => reads.Count == 1, "coalesced latest selection read");
        Check(updates.Count == 0, "obsolete query result is ignored");
        reads.Dequeue().SetResult("latest.json");
        Until(() => updates.Count == 1, "latest selection displayed");
        Check(updates[0] == "latest.json", "latest selection wins");

        follower.RequestRefresh();
        Until(() => reads.Count == 1, "empty selection query");
        reads.Dequeue().SetResult(null);
        Until(() => updates.Count == 2, "empty selection displayed");
        Check(updates[^1] is null, "empty or multiple selection clears stale content");

        follower.RequestRefresh();
        Until(() => reads.Count == 1, "query before closing");
        follower.Stop();
        reads.Dequeue().SetResult("late.png");
        PumpDispatcher(TimeSpan.FromMilliseconds(100));
        var countAfterStop = queryCount;
        follower.RequestRefresh();
        PumpDispatcher(TimeSpan.FromMilliseconds(150));
        Check(updates.Count == 2 && queryCount == countAfterStop, "closed preview ignores results and has no polling");

        follower.Start("reopened.png");
        Until(() => reads.Count == 1, "reopened query");
        follower.Stop();
        follower.Start("new-session.png");
        reads.Dequeue().SetResult("previous-session.png");
        Until(() => reads.Count == 1, "new session query");
        Check(updates.Count == 2, "close and reopen invalidates pending results");
        reads.Dequeue().SetException(new System.Runtime.InteropServices.COMException("Explorer exited"));
        PumpDispatcher(TimeSpan.FromMilliseconds(50));
        follower.RequestRefresh();
        Until(() => reads.Count == 1, "retry after Explorer error");
        source = new IntPtr(99);
        reads.Dequeue().SetResult("wrong-window.png");
        PumpDispatcher(TimeSpan.FromMilliseconds(50));
        Check(updates.Count == 2, "foreground changes invalidate a result even without another event");

        allowed = false;
        var beforeDialog = queryCount;
        follower.RequestRefresh();
        PumpDispatcher(TimeSpan.FromMilliseconds(150));
        Check(queryCount == beforeDialog, "program picker blocks selection updates");
        allowed = true;
        source = IntPtr.Zero;
        follower.RequestRefresh();
        PumpDispatcher(TimeSpan.FromMilliseconds(150));
        Check(queryCount == beforeDialog, "other apps and text fields are not queried");
        source = new IntPtr(99);
        follower.RequestRefresh();
        Until(() => reads.Count == 1, "query after returning to Explorer");
        reads.Dequeue().SetResult("recovered.jpg");
        Until(() => updates.Count == 3, "recovered preview");
        Check(updates[^1] == "recovered.jpg", "preview recovers after errors and focus changes");
    }

    private static void Until(Func<bool> ready, string operation)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(operation);
            PumpDispatcher(TimeSpan.FromMilliseconds(15));
        }
    }
}
