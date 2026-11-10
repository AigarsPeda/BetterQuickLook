using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfImage = System.Windows.Controls.Image;

using BetterQuickLook;

internal static partial class Program
{
    private sealed record PickerSample(double PreviewMs, double MenuMs, long PreviewAllocatedBytes, double PreviewCpuMs);

    private static int BenchmarkAppPicker(string output)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BetterQuickLook-picker-" + Guid.NewGuid().ToString("N")));
        try
        {
            var image = Path.Combine(folder.FullName, "preview.png");
            using (var bitmap = new Bitmap(640, 480)) bitmap.Save(image, ImageFormat.Png);
            var saved = Path.Combine(folder.FullName, "saved.json");
            var unset = Path.Combine(folder.FullName, "unset.json");
            var handlers = ShellFileHandlers.ListAsync(".png").GetAwaiter().GetResult();
            var handler = handlers.FirstOrDefault(item => Path.IsPathFullyQualified(item.Name) && File.Exists(item.Name)) ?? handlers.First();
            FileOpener.SaveHandler(".png", handler.Name, handler.DisplayName, saved);
            var samples = new List<object>();
            for (var i = 0; i < 9; i++)
            {
                foreach (var settings in new[] { unset, saved })
                {
                    var samplePath = Path.Combine(folder.FullName, "sample.json");
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { "--app-picker-sample", image, settings, samplePath }) start.ArgumentList.Add(arg);
                    using var child = Process.Start(start)!;
                    if (!child.WaitForExit(15000))
                    {
                        child.Kill();
                        throw new TimeoutException("Preview sample did not finish.");
                    }
                    if (child.ExitCode != 0) throw new InvalidOperationException($"Preview sample failed ({child.ExitCode}).");
                    samples.Add(new { Saved = settings == saved, Result = JsonSerializer.Deserialize<PickerSample>(File.ReadAllText(samplePath)) });
                }
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(),
                Bitness = IntPtr.Size * 8, SavedApp = handler.DisplayName, HandlerCount = handlers.Count, Samples = samples },
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { folder.Delete(recursive: true); }
    }

    private static int AppPickerSample(string image, string settings, string output)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var preview = new PreviewWindow(settings);
        try
        {
            // Warm WPF and the decoder without asking Windows for PNG handlers in this process.
            _ = PreviewWindow.DecodeImage(image);
            preview.ShowFile(Path.GetDirectoryName(image)!);
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            preview.Hide();
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var timer = Stopwatch.StartNew();
            preview.ShowFile(image);
            WaitForPickerState(() => FindVisual<WpfImage>(preview, item => item.IsVisible && item.ActualWidth > 100) is not null);
            var previewMs = timer.Elapsed.TotalMilliseconds;
            // Include background work that starts shortly after the preview appears.
            while (timer.ElapsedMilliseconds < 500) PumpDispatcher(TimeSpan.FromMilliseconds(10));
            var previewAllocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            var previewCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            timer.Restart();
            preview.ChangeProgramButton.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
            WaitForPickerState(() => preview.ProgramMenu?.Items.OfType<MenuItem>().Any(item => item.Icon is WpfImage) == true);
            File.WriteAllText(output, JsonSerializer.Serialize(new PickerSample(previewMs, timer.Elapsed.TotalMilliseconds, previewAllocated, previewCpu)));
            return 0;
        }
        finally
        {
            preview.ClosePermanently();
            app.Shutdown();
        }
    }

    private static void WaitForPickerState(Func<bool> ready)
    {
        var deadline = Stopwatch.StartNew();
        while (!ready())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Preview or program menu did not become ready.");
            PumpDispatcher(TimeSpan.FromMilliseconds(1));
        }
    }
}
