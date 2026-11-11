using System.Drawing;
using System.IO;
using BetterQuickLook;

internal static partial class Program
{
    private static void ProgramIconCache()
    {
        var path = Path.Combine(_folder, "test-icon.ico");
        using (var file = File.Create(path)) SystemIcons.Application.Save(file);
        var first = ProgramIcons.Extract(path);
        Check(first is { IsFrozen: true }, "icon can be shared safely across threads");
        Check(ReferenceEquals(first, ProgramIcons.Extract(path.ToUpperInvariant())), "Windows path casing shares the decoded icon");
        Check(ReferenceEquals(first, Task.Run(() => ProgramIcons.Extract(path)).GetAwaiter().GetResult()), "background calls reuse the same frozen image");
        using (var file = File.Create(path)) SystemIcons.Warning.Save(file);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
        Check(!ReferenceEquals(first, ProgramIcons.Extract(path)), "replaced icon file invalidates the cache");
        File.Delete(path);
        Check(ProgramIcons.Extract(path) is null, "deleted icon is not returned from cache");
        Check(ProgramIcons.Extract(null) is null && ProgramIcons.Extract(" ") is null, "missing icon locations remain valid fallbacks");
    }
}
