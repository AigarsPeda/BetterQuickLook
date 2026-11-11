namespace BetterQuickLook;

internal static class PreviewWork
{
    // WIC and ZIP reads cannot be interrupted mid-call. Let one finish, but never
    // start queued work for a preview the user has already closed or replaced.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<T> RunAsync<T>(Func<T> read, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = read();
                if (cancellationToken.IsCancellationRequested)
                {
                    if (result is IDisposable disposable) disposable.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
