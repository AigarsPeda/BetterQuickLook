using System.Diagnostics;
using System.IO;
using System.Windows;

namespace BetterQuickLook;

internal sealed partial class PreviewWindow
{
    private WorkspacePreviewView? _workspaceView;
    private string? _shellOpenPath;

    private void ShowShortcut(ShortcutInfo shortcut)
    {
        var description = $"Target: {shortcut.Target ?? "Windows Shell item"}";
        if (!string.IsNullOrWhiteSpace(shortcut.Description)) description += "\n" + shortcut.Description;
        if (!string.IsNullOrWhiteSpace(shortcut.Arguments)) description += "\nArguments: " + shortcut.Arguments;
        if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)) description += "\nStart in: " + shortcut.WorkingDirectory;
        if (shortcut.Links > 1) description += $"\nFollowed {shortcut.Links} shortcuts";
        _shortcutDetails.Text = description;
        _shortcutDetails.ToolTip = description;
        _shortcutDetails.Visibility = Visibility.Visible;
        _body.Children.Clear();
        switch (shortcut.Status)
        {
            case ShortcutStatus.File:
                ShowContent(shortcut.Target!);
                // Executable/command shortcuts keep their saved arguments and
                // working directory when explicitly opened, never during preview.
                if (Path.GetExtension(shortcut.Target!).ToLowerInvariant() is ".exe" or ".com" or ".cmd" or ".bat" or ".ps1")
                    OfferShortcut(shortcut.ShortcutPath);
                break;
            case ShortcutStatus.Folder:
                ShowFolder(shortcut.Target!);
                OfferShortcut(shortcut.ShortcutPath);
                break;
            case ShortcutStatus.Network:
                ShowMessage("Network target. Open the shortcut to connect; it is not previewed automatically.");
                OfferShortcut(shortcut.ShortcutPath);
                break;
            case ShortcutStatus.Virtual:
                ShowMessage("This shortcut points to a Windows item rather than a regular file or folder.");
                OfferShortcut(shortcut.ShortcutPath);
                break;
            case ShortcutStatus.Cycle:
                ShowMessage("These shortcuts point back to each other. No target can be previewed.");
                break;
            case ShortcutStatus.TooDeep:
                ShowMessage($"Stopped after {ShortcutPreview.MaxLinks} shortcuts. The chain is too long to preview.");
                break;
            case ShortcutStatus.Missing:
                ShowMessage("The shortcut target is missing or inaccessible.");
                break;
            default:
                ShowMessage("The shortcut is damaged or its target could not be read.");
                break;
        }
    }

    private void OfferShortcut(string path)
    {
        _currentFilePath = null;
        _shellOpenPath = path;
        _openLabel.Text = "Open shortcut";
        _openButton.ToolTip = "Open shortcut (Ctrl+Enter)";
        System.Windows.Automation.AutomationProperties.SetName(_openButton, "Open shortcut");
        _openButton.Visibility = Visibility.Visible;
        _changeProgramButton.Visibility = Visibility.Collapsed;
    }

    private async void OpenShortcut()
    {
        if (_shellOpenPath is not { } path || _isLaunchingProgram) return;
        var request = ++_launchRequest;
        SetProgramLaunching(true);
        try
        {
            await Task.Run(() => { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); });
            if (request == _launchRequest && IsVisible) Hide();
        }
        catch (Exception error)
        {
            if (request == _launchRequest && IsVisible)
                System.Windows.MessageBox.Show(this, error.Message, "Could not open shortcut");
        }
        finally { if (request == _launchRequest) SetProgramLaunching(false); }
    }
}
