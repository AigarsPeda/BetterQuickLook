using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BetterQuickLook;

internal static partial class Program
{
    private static void ProgramLaunchDelivery()
    {
        Check(ProgramLauncher.IsBetterScreenShot(new(@"C:\apps\BetterScreenShot.exe", null, "Editor")), "executable recognized");
        Check(ProgramLauncher.IsBetterScreenShot(new(null, @"C:\apps\BETTERSCREENSHOT.EXE", "Editor")), "Shell handler recognized");
        Check(!ProgramLauncher.IsBetterScreenShot(new(@"C:\apps\other.exe", null, "BetterScreenShot")), "labels must not reroute another app");
        Check(!ProgramLauncher.IsBetterScreenShot(new(null, "BetterScreenShot.exe", "BetterScreenShot")), "ambiguous handler must use Shell");
        Check(!ProgramLauncher.TrySendAsync("BQL-missing-" + Guid.NewGuid(), "missing.png").GetAwaiter().GetResult(), "absent listener falls back");
        foreach (var response in new[] { "OK", "ERROR", "DISCONNECT", "TIMEOUT" })
        {
            var pipeName = "BQL-tests-" + Guid.NewGuid();
            var path = Path.Combine(_folder, "attēls with spaces.png");
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var receive = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                using (var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true))
                {
                    var request = JsonSerializer.Deserialize<string[]>((await reader.ReadLineAsync())!);
                    Check(request!.SequenceEqual(new[] { path }), "exact Unicode path delivered once");
                    if (response == "TIMEOUT") await Task.Delay(200);
                    else if (response != "DISCONNECT")
                    {
                        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                        await writer.WriteLineAsync(response);
                    }
                }
                server.Disconnect();
            });
            if (response == "OK") Check(ProgramLauncher.TrySendAsync(pipeName, path).GetAwaiter().GetResult(), "acknowledged delivery");
            else
            {
                var failed = false;
                try { ProgramLauncher.TrySendAsync(pipeName, path, 100).GetAwaiter().GetResult(); }
                catch (IOException) { failed = true; }
                Check(failed, "uncertain delivery must throw rather than request fallback: " + response);
            }
            receive.GetAwaiter().GetResult();
        }
    }
}
