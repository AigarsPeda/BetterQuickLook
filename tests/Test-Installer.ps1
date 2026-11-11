param(
    [Parameter(Mandatory)][string]$MsiPath,
    [switch]$LeaveInstalled
)

$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$markerKey = 'HKCU:\Software\BetterQuickLook\Installer'
$installedExe = Join-Path $env:LOCALAPPDATA 'Programs\BetterQuickLook\BetterQuickLook.exe'
$existing = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' |
    Where-Object DisplayName -eq 'BetterQuickLook'
if ($existing -or (Test-Path -LiteralPath $installedExe) -or
    (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).BetterQuickLook) {
    throw 'Use a clean test account. This test will not replace an existing installation or startup entry.'
}
if (Get-Process BetterQuickLook -ErrorAction SilentlyContinue) { throw 'Exit BetterQuickLook before this test.' }

function Invoke-Setup([string]$action) {
    $log = Join-Path $env:TEMP ('BetterQuickLook-setup-' + [guid]::NewGuid().ToString('N') + '.log')
    $process = Start-Process msiexec.exe -ArgumentList "$action `"$MsiPath`" /qn /norestart /l*v `"$log`"" -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -notin @(0, 3010)) { throw "Setup failed: $($process.ExitCode). Log: $log" }
}
function Assert-Startup {
    $command = Get-ItemPropertyValue $runKey -Name BetterQuickLook
    if ($command -cne ('"' + $installedExe + '"')) { throw "Unexpected startup command: $command" }
    if (!(Test-Path -LiteralPath $installedExe)) { throw 'Installed executable is missing.' }
    if ((Get-ItemPropertyValue $markerKey -Name Installed) -ne 1) { throw 'Install marker is missing.' }
}

Invoke-Setup '/i'
Assert-Startup
Write-Output 'PASS install registers the quoted installed executable for current-user startup'
Invoke-Setup '/fa'
Assert-Startup
Write-Output 'PASS repair preserves startup registration'
Invoke-Setup '/x'
if ((Get-ItemProperty $runKey -ErrorAction SilentlyContinue).BetterQuickLook) { throw 'Uninstall left startup registration behind.' }
if (Test-Path -LiteralPath $installedExe) { throw 'Uninstall left the executable behind.' }
Write-Output 'PASS uninstall removes the startup entry and installed executable'
if ($LeaveInstalled) {
    Invoke-Setup '/i'
    Assert-Startup
    Write-Output 'PASS reinstallation registers startup again; application left installed'
}
