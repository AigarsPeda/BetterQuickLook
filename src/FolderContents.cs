using System.IO;
using System.IO.Enumeration;

namespace BetterQuickLook;

internal sealed record FolderItem(string Path, string Name, bool IsFolder, long Bytes, DateTime Modified);
internal sealed record FolderContentsResult(FolderItem[] Items, bool HasMore);

internal static class FolderContents
{
    internal const int Limit = 20;

    internal static FolderContentsResult Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entries = new FileSystemEnumerable<FolderItem>(path,
            (ref FileSystemEntry item) => new(item.ToFullPath(), item.FileName.ToString(),
                item.IsDirectory, item.Length, item.LastWriteTimeUtc.LocalDateTime),
            new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false });
        var items = new List<FolderItem>(Limit);
        var more = false;
        foreach (var item in entries)
        {
            token.ThrowIfCancellationRequested();
            if (items.Count == Limit) { more = true; break; }
            items.Add(item);
        }
        // Sort only the bounded sample, never enumerate a whole large directory.
        return new(items.OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), more);
    }
}
