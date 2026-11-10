using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BetterQuickLook;

internal static class ImageEditor
{
    private sealed record Settings(string? ImageEditorPath = null, string? HandlerName = null, string? DisplayName = null);
    internal sealed record Selection(string? EditorPath, string? HandlerName, string DisplayName);

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterQuickLook", "settings.json");

    internal static Selection? LoadSelection(string? settingsPath = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath ?? SettingsPath));
            if (!string.IsNullOrWhiteSpace(settings?.HandlerName) && !string.IsNullOrWhiteSpace(settings.DisplayName))
                return new Selection(null, settings.HandlerName, settings.DisplayName);
            return settings?.ImageEditorPath is { } path && File.Exists(path) &&
                   Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? new Selection(path, null, DisplayName(path)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static string? Load(string? settingsPath = null) => LoadSelection(settingsPath)?.EditorPath;

    internal static void Save(string editorPath, string? settingsPath = null)
    {
        if (!File.Exists(editorPath) || !Path.GetExtension(editorPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Choose an editor executable (.exe).", editorPath);

        WriteSettings(new Settings(ImageEditorPath: editorPath), settingsPath);
    }

    internal static void SaveHandler(string handlerName, string displayName, string? settingsPath = null)
    {
        if (string.IsNullOrWhiteSpace(handlerName) || string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Choose an available program.");
        WriteSettings(new Settings(HandlerName: handlerName, DisplayName: displayName), settingsPath);
    }

    private static void WriteSettings(Settings settings, string? settingsPath)
    {
        var path = settingsPath ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
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
