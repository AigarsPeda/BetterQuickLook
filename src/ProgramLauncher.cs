using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BetterQuickLook;

internal static class ProgramLauncher
{
    internal static Task OpenAsync(FileOpener.Selection selection, string filePath) => Task.Run(async () =>
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("File was not found.", filePath);
        if (IsBetterScreenShot(selection))
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var currentProcess = Process.GetCurrentProcess();
            var name = "BetterScreenShot-" + identity.User!.Value;
            // Avoid a connection timeout on cold launches. Older versions without
            // the image pipe still use the normal Windows launch path below.
            if (Mutex.TryOpenExisting(@"Local\" + name, out var instance))
            {
                using (instance)
                    if (await TrySendAsync(name + "-" + currentProcess.SessionId + "-Images", filePath)
                        .ConfigureAwait(false)) return;
            }
        }
        if (selection.HandlerName is { } handler)
            await ShellFileHandlers.OpenAsync(Path.GetExtension(filePath), handler, selection.DisplayName, filePath).ConfigureAwait(false);
        else
        {
            using var process = Process.Start(FileOpener.CreateStartInfo(selection.ExecutablePath!, filePath));
        }
    });

    internal static bool IsBetterScreenShot(FileOpener.Selection selection)
    {
        var path = selection.HandlerName ?? selection.ExecutablePath;
        return path is not null && Path.IsPathFullyQualified(path) &&
               Path.GetFileName(path).Equals("BetterScreenShot.exe", StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<bool> TrySendAsync(string pipeName, string filePath, int acknowledgementTimeoutMs = 10000)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var connectTimeout = new CancellationTokenSource(150))
        {
            try { await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
            { return false; }
        }
        // Once writing starts, delivery is uncertain on failure. Never launch a
        // second copy as a fallback: it could open the same image twice.
        using var timeout = new CancellationTokenSource(acknowledgementTimeoutMs);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new[] { Path.GetFullPath(filePath) }).AsMemory(), timeout.Token).ConfigureAwait(false);
            if (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) != "OK")
                throw new IOException("BetterScreenShot did not confirm the image request. Check its window before trying again.");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw new IOException("BetterScreenShot did not respond in time. Check its window before trying again.");
        }
    }
}
