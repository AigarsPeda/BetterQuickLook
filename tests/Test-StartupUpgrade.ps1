param([Parameter(Mandatory)][string]$MsiPath)

$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$installedExe = Join-Path $env:LOCALAPPDATA 'Programs\BetterQuickLook\BetterQuickLook.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvalKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'

function Get-StartupApproval {
    $value = (Get-ItemProperty -LiteralPath $approvalKey -ErrorAction SilentlyContinue).BetterQuickLook
    if ($null -eq $value) { return $null }
    return [Convert]::ToBase64String($value)
}
function Get-InstalledProduct {
    $keys = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $products = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq 'BetterQuickLook' | Sort-Object PSChildName -Unique)
    if ($products.Count -ne 1) { throw "Expected one installed BetterQuickLook product, found $($products.Count)." }
    return $products[0]
}
function Assert-Startup {
    $command = Get-ItemPropertyValue -LiteralPath $runKey -Name BetterQuickLook
    if ($command -cne ('"' + $installedExe + '"')) { throw "Unexpected startup command: $command" }
    if (!(Test-Path -LiteralPath $installedExe -PathType Leaf)) { throw 'Installed executable is missing.' }
}

Assert-Startup
$before = Get-InstalledProduct
$approval = Get-StartupApproval
if ($null -ne $approval -and [BitConverter]::ToUInt32([Convert]::FromBase64String($approval), 0) -ne 2) {
    throw 'This test requires startup to be enabled before the upgrade. It does not change your preference.'
}
$running = @(Get-Process BetterQuickLook -ErrorAction SilentlyContinue | Where-Object Path -eq $installedExe)
$log = Join-Path $env:TEMP ('BetterQuickLook-upgrade-' + [guid]::NewGuid().ToString('N') + '.log')
try {
    $running | Stop-Process
    $process = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $MsiPath + '"'), '/qn', '/norestart', '/l*v', ('"' + $log + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -notin @(0, 3010)) { throw "Upgrade failed: $($process.ExitCode). Log: $log" }
    # Verify the installer's result without repairing or rewriting the Run value.
    Assert-Startup
    if ((Get-StartupApproval) -cne $approval) { throw "Windows startup approval changed. Log: $log" }
    $after = Get-InstalledProduct
    if ($after.PSChildName -eq $before.PSChildName) {
        throw 'No product replacement occurred. Build a new MSI before testing an upgrade.'
    }
    if ([version]$after.DisplayVersion -lt [version]$before.DisplayVersion) { throw 'Upgrade installed an older version.' }
    Write-Output "PASS upgrade $($before.DisplayVersion) -> $($after.DisplayVersion) keeps startup enabled and the installed path registered"
    Remove-Item -LiteralPath $log
}
finally {
    if ($running.Count -gt 0 -and (Test-Path -LiteralPath $installedExe)) {
        Start-Process -FilePath $installedExe -WindowStyle Hidden
    }
}
