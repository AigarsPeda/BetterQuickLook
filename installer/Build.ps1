$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\BetterQuickLook.csproj'
$payload = Join-Path $PSScriptRoot 'obj\payload'
$output = Join-Path $projectRoot 'dist'

dotnet publish $project -c Release -r win-x86 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Publishing BetterQuickLook failed.' }

$exe = Get-Item -LiteralPath (Join-Path $payload 'BetterQuickLook.exe')
$version = [version]$exe.VersionInfo.FileVersion
dotnet build (Join-Path $PSScriptRoot 'BetterQuickLook.Setup.wixproj') -c Release -p:AppVersion=$($version.ToString(3)) -o $output
if ($LASTEXITCODE -ne 0) { throw 'Building the BetterQuickLook installer failed.' }

Write-Output (Join-Path $output 'BetterQuickLook-Setup.msi')
