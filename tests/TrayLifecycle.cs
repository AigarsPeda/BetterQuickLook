using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Imaging;
using BetterQuickLook;

internal static partial class Program
{
    private static int TrayLifecycle(string output)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BetterQuickLook-tray-" + Guid.NewGuid().ToString("N")));
        var app = new App();
        app.InitializeComponent();
        var exitCode = 0;
        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var before = await SampleTrayIdle();
                var image = Path.Combine(root.FullName, "image.png");
                using (var bitmap = new Bitmap(800, 600)) bitmap.Save(image, ImageFormat.Png);
                var json = Path.Combine(root.FullName, "sample.json");
                File.WriteAllText(json, JsonSerializer.Serialize(Enumerable.Range(0, 300).Select(i => new { i, name = "Rīga" })));
                var preview = new PreviewWindow(Path.Combine(root.FullName, "settings.json"));
                typeof(App).GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, preview);
                var timer = Stopwatch.StartNew();
                preview.ShowFile(image);
                await WaitForPreview(() => FindVisual<System.Windows.Controls.Image>(preview, item => item.Source is BitmapSource) is not null);
                var firstImageMs = timer.Elapsed.TotalMilliseconds;
                preview.Hide();
                preview.ShowFile(json);
                await WaitForPreview(() => ContainsText(preview, json));
                preview.Hide();
                var after = await SampleTrayIdle();
                // Ensure the tray hook and reusable preview remain functional after idling.
                preview.ShowFile(image);
                await WaitForPreview(() => FindVisual<System.Windows.Controls.Image>(preview, item => item.Source is BitmapSource) is not null);
                preview.Hide();
                File.WriteAllText(output, JsonSerializer.Serialize(new { FirstImageMs = firstImageMs, BeforePreview = before, AfterPreview = after },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
            finally { app.Shutdown(); }
        });
        try { app.Run(); }
        finally { root.Delete(true); }
        return exitCode;
    }

    private static async Task WaitForPreview(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready())
        {
            if (timer.Elapsed.TotalSeconds > 10) throw new TimeoutException("Preview did not appear.");
            await Task.Delay(10);
        }
    }

    private static async Task<object> SampleTrayIdle()
    {
        await Task.Delay(5000);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        var samples = new List<object>();
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000);
            process.Refresh();
            samples.Add(new { Seconds = timer.Elapsed.TotalSeconds, CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                WorkingSetMiB = process.WorkingSet64 / 1048576.0, PrivateCommitMiB = process.PrivateMemorySize64 / 1048576.0,
                ManagedLiveMiB = GC.GetTotalMemory(false) / 1048576.0, Handles = process.HandleCount });
        }
        return samples;
    }
}
