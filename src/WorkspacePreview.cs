using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BetterQuickLook;

internal sealed record WorkspaceFolder(string Name, string Location, string? LocalPath, string? Notice);

internal sealed class WorkspacePreviewData(JsonPreviewData json) : IDisposable
{
    internal JsonPreviewData Json { get; } = json;
    internal List<WorkspaceFolder> Folders { get; } = [];
    internal int FolderCount { get; set; }
    internal int Settings { get; set; }
    internal int Tasks { get; set; }
    internal int LaunchConfigurations { get; set; }
    internal string[] Extensions { get; set; } = [];
    internal int ExtensionCount { get; set; }
    internal string? Notice { get; set; }
    public void Dispose() => Json.Dispose();
}

internal static class WorkspacePreview
{
    internal const int MaxFolders = 100;

    internal static WorkspacePreviewData Read(string path)
    {
        var result = new WorkspacePreviewData(JsonPreviewReader.Read(path));
        try
        {
            if (result.Json.Document is not { } document)
            {
                result.Notice = result.Json.Notice ?? "Workspace details are unavailable.";
                return result;
            }
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.Notice = "A workspace must contain a JSON object with a folders list.";
                return result;
            }
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (root.TryGetProperty("folders", out var folders) && folders.ValueKind == JsonValueKind.Array)
            {
                result.FolderCount = folders.GetArrayLength();
                foreach (var folder in folders.EnumerateArray().Take(MaxFolders))
                    result.Folders.Add(ReadFolder(folder, baseDirectory));
                if (result.FolderCount > MaxFolders) result.Notice = $"Showing the first {MaxFolders} of {result.FolderCount:N0} folders.";
            }
            else result.Notice = "The workspace has no valid folders list.";
            result.Settings = Child(root, "settings") is { ValueKind: JsonValueKind.Object } settings ? settings.EnumerateObject().Count() : 0;
            result.Tasks = ArrayCount(Child(Child(root, "tasks"), "tasks"));
            result.LaunchConfigurations = ArrayCount(Child(Child(root, "launch"), "configurations"));
            var extensions = Child(Child(root, "extensions"), "recommendations");
            result.ExtensionCount = ArrayCount(extensions);
            if (extensions.ValueKind == JsonValueKind.Array)
                result.Extensions = extensions.EnumerateArray().Take(30).Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).ToArray();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static JsonElement Child(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) ? child : default;
    private static int ArrayCount(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;
    private static string? Text(JsonElement value, string name) => Child(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static WorkspaceFolder ReadFolder(JsonElement value, string baseDirectory)
    {
        var name = Text(value, "name");
        var path = Text(value, "path");
        var uriText = Text(value, "uri");
        var location = path ?? uriText ?? "No path provided";
        try
        {
            if (path is null && uriText is not null)
            {
                var colon = uriText.IndexOf(':');
                if (colon > 0 && Uri.CheckSchemeName(uriText[..colon]) &&
                    !uriText[..colon].Equals("file", StringComparison.OrdinalIgnoreCase))
                    return new(name ?? "Remote folder", location, null, "Remote location • not checked");
                if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
                    return new(name ?? "Invalid folder", location, null, "Invalid URI");
                if (!uri.IsFile) return new(name ?? "Remote folder", location, null, "Remote location • not checked");
                path = uri.LocalPath;
            }
            if (string.IsNullOrWhiteSpace(path)) return new(name ?? "Invalid folder", location, null, "Missing folder path");
            if (path.Contains("${", StringComparison.Ordinal)) return new(name ?? "Folder", path, null, "Unresolved path variable");
            path = Path.GetFullPath(path, baseDirectory);
            name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) : name;
            return new(string.IsNullOrEmpty(name) ? path : name, path, path,
                PreviewPath.IsNetwork(path) ? "Network location • not checked" : null);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return new(name ?? "Invalid folder", location, null, "Invalid folder path"); }
    }
}

internal static class PreviewPath
{
    internal static bool IsNetwork(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) ||
        GetDriveType(Path.GetPathRoot(path) ?? "") == 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
    private static extern uint GetDriveType(string root);
}
