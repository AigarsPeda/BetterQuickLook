using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using BetterQuickLook;

internal static class Performance
{
    // Fixture creation and warmup are excluded. Run in Release with no debugger attached.
    internal static int Run(string[] args)
    {
        var output = args[Array.IndexOf(args, "--benchmark") + 1];
        var root = Path.Combine(Path.GetTempPath(), "BetterQuickLook-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = Path.Combine(root, "files");
            Directory.CreateDirectory(folder);
            for (var i = 0; i < 2000; i++) File.WriteAllBytes(Path.Combine(folder, $"{i}.txt"), new byte[128]);
            var json = Path.Combine(root, "data.json");
            File.WriteAllText(json, JsonSerializer.Serialize(Enumerable.Range(0, 15000)
                .Select(i => new { id = i, name = "Rīga sample", enabled = true, values = new[] { 1, 2, 3 } })));
            var picture = Path.Combine(root, "image.jpg");
            using (var bitmap = new Bitmap(4000, 3000)) bitmap.Save(picture, ImageFormat.Jpeg);
            var results = new List<object>();
            Measure("Folder scan: 2,000 files", () => { _ = FolderPreview.Scan(folder, CancellationToken.None); }, results);
            Measure("JSON read: 15,000 objects", () => { using var data = JsonPreviewReader.Read(json); }, results);
            using var preview = JsonPreviewReader.Read(json);
            Measure("JSON view: capped formatted text", () => { _ = new JsonPreviewView(json, preview); }, results);
            Measure("JPEG decode: 4000 x 3000", () => { _ = PreviewWindow.DecodeImage(picture); }, results);
            var report = new { Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(),
                Bitness = IntPtr.Size * 8, Samples = 7, Warmups = 2, JsonBytes = new FileInfo(json).Length,
                TimestampUtc = DateTime.UtcNow, Results = results };
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Measure(string name, Action action, List<object> results)
    {
        for (var i = 0; i < 2; i++) action();
        var times = new List<double>();
        var allocations = new List<long>();
        var cpu = new List<double>();
        using var process = Process.GetCurrentProcess();
        for (var i = 0; i < 7; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var cpuStart = process.TotalProcessorTime;
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            times.Add(timer.Elapsed.TotalMilliseconds);
            cpu.Add((process.TotalProcessorTime - cpuStart).TotalMilliseconds);
        }
        times.Sort(); allocations.Sort(); cpu.Sort();
        results.Add(new { Name = name, MedianMs = times[3], MinMs = times[0], MaxMs = times[^1],
            MedianAllocatedBytes = allocations[3], MedianCpuMs = cpu[3] });
        Console.WriteLine($"{name}: {times[3]:F2} ms, {allocations[3]:N0} allocated bytes");
    }
}
