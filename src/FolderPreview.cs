using System.IO;

namespace BetterQuickLook;

internal readonly record struct FolderScanResult(
    long Bytes, int Files, int Folders, int Skipped, bool ReachedLimit)
{
    public bool IsPartial => Skipped > 0 || ReachedLimit;
}

internal static class FolderPreview
{
    internal const int MaxEntries = 100_000;

    internal static FolderScanResult Scan(string path, CancellationToken cancellationToken, int maxEntries = MaxEntries)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);

        var pending = new Stack<string>();
        pending.Push(path);
        long bytes = 0;
        var files = 0;
        var folders = 0;
        var skipped = 0;
        var scanned = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++scanned > maxEntries)
                        return new FolderScanResult(bytes, files, folders, skipped, true);

                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        var isFolder = (attributes & FileAttributes.Directory) != 0;
                        if (isFolder) folders++;
                        else files++;

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        if (isFolder) pending.Push(entry);
                        else bytes = checked(bytes + new FileInfo(entry).Length);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
                    {
                        skipped++;
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        return new FolderScanResult(bytes, files, folders, skipped, false);
    }
}
