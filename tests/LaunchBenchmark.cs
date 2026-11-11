using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterQuickLook;

internal static partial class Program
{
    private static int LaunchBenchmark(string executable, string mode, string output)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BQL-launch-bench-" + Guid.NewGuid().ToString("N")));
        try
        {
            var previousTest = FindBenchmarkWindow("launch-attēls-");
            if (previousTest != IntPtr.Zero) PostMessage(previousTest, 0x0010, IntPtr.Zero, IntPtr.Zero);
            var pixels = new byte[3840 * 2160 * 4];
            new Random(42).NextBytes(pixels);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var bitmap = BitmapSource.Create(3840, 2160, 96, 96, PixelFormats.Bgra32, null, pixels, 3840 * 4);
            var fixture = Path.Combine(folder.FullName, "fixture.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(fixture)) encoder.Save(stream);
            var results = new List<object>();
            if (mode == "cold")
            {
                var stop = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                stop.ArgumentList.Add("--exit");
                using var command = Process.Start(stop)!;
                if (!command.WaitForExit(15000)) throw new TimeoutException("Exit request did not complete.");
                var deadline = Stopwatch.StartNew();
                while (Process.GetProcessesByName("BetterScreenShot").Any(IsRunning))
                {
                    if (deadline.Elapsed.TotalSeconds > 10) throw new InvalidOperationException("Editor remained open; aborting cold measurement.");
                    Thread.Sleep(50);
                }
            }
            for (var i = 0; i < (mode == "cold" ? 1 : 5); i++)
            {
                var file = Path.Combine(folder.FullName, "launch-attēls-" + Guid.NewGuid().ToString("N") + ".png");
                File.Copy(fixture, file);
                IntPtr window = IntPtr.Zero;
                try
                {
                    var timer = Stopwatch.StartNew();
                    var selection = new FileOpener.Selection(null, executable, "BetterScreenShot");
                    var launch = mode == "direct" ? ProgramLauncher.OpenAsync(selection, file) :
                        ShellFileHandlers.OpenAsync(".png", executable, "BetterScreenShot", file);
                    double? visibleMs = null;
                    double? handoffMs = null;
                    while (visibleMs is null || handoffMs is null)
                    {
                        if (launch.IsCompleted && handoffMs is null)
                        {
                            launch.GetAwaiter().GetResult();
                            handoffMs = timer.Elapsed.TotalMilliseconds;
                        }
                        if (visibleMs is null)
                        {
                            window = FindBenchmarkWindow(Path.GetFileName(file));
                            if (window != IntPtr.Zero) visibleMs = timer.Elapsed.TotalMilliseconds;
                        }
                        if (timer.Elapsed.TotalSeconds > 20) throw new TimeoutException("Editor did not appear.");
                        if (visibleMs is null || handoffMs is null) Thread.Sleep(10);
                    }
                    results.Add(new { HandoffMs = handoffMs, VisibleWindowMs = visibleMs });
                }
                finally
                {
                    if (window != IntPtr.Zero) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    var close = Stopwatch.StartNew();
                    while (FindBenchmarkWindow(Path.GetFileName(file)) != IntPtr.Zero && close.Elapsed.TotalSeconds < 5) Thread.Sleep(20);
                }
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { Mode = mode, Samples = results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        finally { folder.Delete(true); }

        bool IsRunning(Process process)
        {
            using (process) return !process.HasExited;
        }
    }

    private static IntPtr FindBenchmarkWindow(string filename)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            var text = new StringBuilder(512);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString().Contains(filename, StringComparison.Ordinal)) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
