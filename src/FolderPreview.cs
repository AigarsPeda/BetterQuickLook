using System.IO;
using System.IO.Enumeration;

namespace BetterQuickLook;

internal readonly record struct FolderScanResult(
    long Bytes, int Files, int Folders, int Skipped, bool ReachedLimit)
{
    public bool IsPartial => Skipped > 0 || ReachedLimit;
}

internal static class FolderPreview
{
    internal const int MaxEntries = 100_000;
    private readonly record struct Entry(string? DirectoryPath, FileAttributes Attributes, long Length);
    private static readonly EnumerationOptions Options = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

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
                // Enumeration already supplies attributes and file lengths on Windows.
                var entries = new FileSystemEnumerable<Entry>(current,
                    (ref FileSystemEntry item) => new Entry(item.IsDirectory ? item.ToFullPath() : null,
                        item.Attributes, item.Length), Options);
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++scanned > maxEntries)
                        return new FolderScanResult(bytes, files, folders, skipped, true);

                    try
                    {
                        var attributes = entry.Attributes;
                        var isFolder = (attributes & FileAttributes.Directory) != 0;
                        if (isFolder) folders++;
                        else files++;

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        if (isFolder) pending.Push(entry.DirectoryPath!);
                        else bytes = checked(bytes + entry.Length);
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
