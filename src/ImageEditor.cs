using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BetterQuickLook;

internal static class ImageEditor
{
    private sealed record Settings(string ImageEditorPath);

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterQuickLook", "settings.json");

    internal static string? Load(string? settingsPath = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath ?? SettingsPath));
            return settings is not null && File.Exists(settings.ImageEditorPath) &&
                   Path.GetExtension(settings.ImageEditorPath).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? settings.ImageEditorPath : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static void Save(string editorPath, string? settingsPath = null)
    {
        if (!File.Exists(editorPath) || !Path.GetExtension(editorPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Choose an editor executable (.exe).", editorPath);

        var path = settingsPath ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(editorPath)));
        File.Move(temporary, path, true);
    }

    internal static ProcessStartInfo CreateStartInfo(string editorPath, string imagePath)
    {
        if (!File.Exists(editorPath)) throw new FileNotFoundException("Image editor was not found.", editorPath);
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Image was not found.", imagePath);
        var start = new ProcessStartInfo(editorPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(editorPath)!
        };
        start.ArgumentList.Add(imagePath);
        return start;
    }

    internal static string DisplayName(string editorPath) =>
        Path.GetFileName(editorPath).Equals("mspaint.exe", StringComparison.OrdinalIgnoreCase)
            ? "Paint" : Path.GetFileNameWithoutExtension(editorPath);

    internal static string? FindPaint()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new List<string>
        {
            Path.Combine(windows, "Sysnative", "mspaint.exe"),
            Path.Combine(windows, "System32", "mspaint.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "mspaint.exe")
        };
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, "mspaint.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }
}
