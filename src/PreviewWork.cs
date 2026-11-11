namespace BetterQuickLook;

internal static class PreviewWork
{
    // WIC and ZIP reads cannot be interrupted mid-call. Let one finish, but never
    // start queued work for a preview the user has already closed or replaced.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<T> RunAsync<T>(Func<T> read, CancellationToken cancellationToken, bool sta = false)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            T Read()
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = read();
                if (cancellationToken.IsCancellationRequested)
                {
                    if (result is IDisposable disposable) disposable.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return result;
            }
            if (!sta) return await Task.Run(Read, cancellationToken).ConfigureAwait(false);
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { completion.SetResult(Read()); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                { completion.SetCanceled(cancellationToken); }
                catch (Exception error) { completion.SetException(error); }
            }) { IsBackground = true, Name = "BetterQuickLook shortcut" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return await completion.Task.ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
