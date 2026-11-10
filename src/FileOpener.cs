using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BetterQuickLook;

internal static class FileOpener
{
    internal sealed record Selection(string? ExecutablePath, string? HandlerName, string DisplayName);

    // The old fields keep image choices made before per-extension preferences were added.
    private sealed record Settings(
        Dictionary<string, Selection>? Programs = null,
        string? ImageEditorPath = null,
        string? HandlerName = null,
        string? DisplayName = null);

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterQuickLook", "settings.json");

    internal static Selection? LoadSelection(string extension, string? settingsPath = null)
    {
        var settings = ReadSettings(settingsPath);
        var key = PreferenceKey(extension);
        if (settings.Programs?.TryGetValue(key, out var selection) == true && selection is not null)
            return Valid(selection) ? selection : null;
        if (key != "images") return null;
        if (!string.IsNullOrWhiteSpace(settings.HandlerName) && !string.IsNullOrWhiteSpace(settings.DisplayName))
            return new Selection(null, settings.HandlerName, settings.DisplayName);
        return settings.ImageEditorPath is { } path && File.Exists(path) &&
               Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? new Selection(path, null, DisplayName(path)) : null;
    }

    internal static void SaveExecutable(string extension, string executablePath, string? settingsPath = null)
    {
        if (!File.Exists(executablePath) || !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Choose a program executable (.exe).", executablePath);
        SaveSelection(extension, new Selection(executablePath, null, DisplayName(executablePath)), settingsPath);
    }

    internal static void SaveHandler(string extension, string handlerName, string displayName, string? settingsPath = null)
    {
        if (string.IsNullOrWhiteSpace(handlerName) || string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Choose an available program.");
        SaveSelection(extension, new Selection(null, handlerName, displayName), settingsPath);
    }

    private static void SaveSelection(string extension, Selection selection, string? settingsPath)
    {
        var path = settingsPath ?? SettingsPath;
        var settings = ReadSettings(path);
        var programs = settings.Programs is null
            ? new Dictionary<string, Selection>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Selection>(settings.Programs, StringComparer.OrdinalIgnoreCase);
        programs[PreferenceKey(extension)] = selection;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings with { Programs = programs }));
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, path, true); break; }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && attempt < 3)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static Settings ReadSettings(string? settingsPath)
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath ?? SettingsPath)) ?? new Settings(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    private static bool Valid(Selection selection) =>
        !string.IsNullOrWhiteSpace(selection.DisplayName) &&
        (selection.HandlerName is { Length: > 0 } ||
         selection.ExecutablePath is { } path && File.Exists(path) &&
         Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase));

    internal static bool IsImageExtension(string extension) => extension.ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" or
        ".webp" or ".heic" or ".heif" or ".avif" or ".ico" or ".wdp" or ".jxr";

    private static string PreferenceKey(string extension)
    {
        var key = extension.ToLowerInvariant();
        if (IsImageExtension(key)) return "images";
        return key switch
        {
            ".json" or ".jsonc" => "json",
            ".doc" or ".docx" or ".docm" => "word",
            ".xls" or ".xlsx" or ".xlsm" or ".xlsb" => "excel",
            _ => key
        };
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string filePath)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("Program was not found.", executablePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException("File was not found.", filePath);
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        start.ArgumentList.Add(filePath);
        return start;
    }

    internal static string DisplayName(string executablePath) =>
        Path.GetFileName(executablePath).Equals("mspaint.exe", StringComparison.OrdinalIgnoreCase)
            ? "Paint" : Path.GetFileNameWithoutExtension(executablePath);

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
