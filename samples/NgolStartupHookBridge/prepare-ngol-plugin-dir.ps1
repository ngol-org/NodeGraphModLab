# prepare-ngol-plugin-dir.ps1 - NgolStartupHookBridge ngol-plugin layout
#
# Builds NodeGraphModLab.Core (net6.0) / NodeAPI / BuiltinNodes / HostLogging, copies outputs
# + WebUI/dist into ngol-plugin/. Does not overwrite ngol-config.json (tracked file).
#
# Usage:
#   .\prepare-ngol-plugin-dir.ps1
#   .\prepare-ngol-plugin-dir.ps1 -SkipBuild

param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
$SampleRoot = $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $SampleRoot)
$Dist = Join-Path $SampleRoot "ngol-plugin"

Write-Host ""
Write-Host "=== NgolStartupHookBridge ngol-plugin layout ===" -ForegroundColor Cyan
Write-Host "  RepoRoot : $RepoRoot" -ForegroundColor DarkGray
Write-Host "  Dist     : $Dist" -ForegroundColor DarkGray

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "[1/3] NodeGraphModLab.Core (net6.0, Release) build..." -ForegroundColor Yellow
    dotnet build (Join-Path $RepoRoot "NodeGraphModLab.Core\NodeGraphModLab.Core.csproj") -c Release -f net6.0
    if ($LASTEXITCODE -ne 0) { throw "Core build failed" }

    Write-Host ""
    Write-Host "[2/3] NodeGraphModLab.BuiltinNodes (netstandard2.0, Release) build..." -ForegroundColor Yellow
    dotnet build (Join-Path $RepoRoot "NodeGraphModLab.BuiltinNodes\NodeGraphModLab.BuiltinNodes.csproj") -c Release -f netstandard2.0
    if ($LASTEXITCODE -ne 0) { throw "BuiltinNodes build failed" }

    Write-Host ""
    Write-Host "[3/3] NodeGraphModLab.HostLogging (net6.0, Release) build..." -ForegroundColor Yellow
    dotnet build (Join-Path $RepoRoot "NodeGraphModLab.HostLogging\NodeGraphModLab.HostLogging.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "HostLogging build failed" }
} else {
    Write-Host ""
    Write-Host "Skip build (use existing artifacts)" -ForegroundColor Gray
}

New-Item -ItemType Directory -Path $Dist -Force | Out-Null

$coreOut = Join-Path $RepoRoot "NodeGraphModLab.Core\bin\Release\net6.0"
if (-not (Test-Path $coreOut)) { throw "Core build output not found: $coreOut" }

Write-Host ""
Write-Host "Copying Core output..." -ForegroundColor Yellow
Get-ChildItem $coreOut -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName $Dist -Force
    Write-Host "  Copied: $($_.Name)" -ForegroundColor DarkCyan
}
Get-ChildItem $coreOut -Directory | ForEach-Object {
    Copy-Item $_.FullName -Destination $Dist -Recurse -Force
    Write-Host "  Copied: $($_.Name)/ (satellite resources)" -ForegroundColor DarkGray
}

$hostLoggingSrc = Join-Path $RepoRoot "NodeGraphModLab.HostLogging\bin\Release\net6.0\NodeGraphModLab.HostLogging.dll"
if (Test-Path $hostLoggingSrc) {
    Copy-Item $hostLoggingSrc $Dist -Force
    Write-Host "  Copied: NodeGraphModLab.HostLogging.dll" -ForegroundColor DarkCyan
} else {
    Write-Warning "HostLogging DLL not found: $hostLoggingSrc"
}

$builtinSrc = Join-Path $RepoRoot "NodeGraphModLab.BuiltinNodes\bin\Release\netstandard2.0\NodeGraphModLab.BuiltinNodes.dll"
$builtinDest = Join-Path $Dist "Nodes\Builtin"
New-Item -ItemType Directory -Path $builtinDest -Force | Out-Null
if (Test-Path $builtinSrc) {
    Copy-Item $builtinSrc $builtinDest -Force
    Write-Host "  Copied: NodeGraphModLab.BuiltinNodes.dll -> Nodes/Builtin/" -ForegroundColor DarkCyan
} else {
    Write-Warning "BuiltinNodes DLL not found: $builtinSrc"
}

New-Item -ItemType Directory -Path (Join-Path $Dist "Nodes\CustomNodes\cs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Dist "Nodes\CustomNodes\dll") -Force | Out-Null

Write-Host ""
Write-Host "WebUI dist..." -ForegroundColor Yellow
$webUiDist = Join-Path $RepoRoot "WebUI\dist"
$webUiDest = Join-Path $Dist "WebUI"
if (Test-Path $webUiDist) {
    New-Item -ItemType Directory -Path $webUiDest -Force | Out-Null
    Copy-Item "$webUiDist\*" $webUiDest -Recurse -Force
    Write-Host "  Copied: WebUI/dist -> WebUI/" -ForegroundColor DarkCyan
} else {
    Write-Warning "WebUI/dist not found. Run: cd WebUI; npm run build"
}

$configPath = Join-Path $Dist "ngol-config.json"
if (-not (Test-Path $configPath)) {
    # デフォルトポート(11156)を使用。他の起動中NGOLインスタンス(ゲーム等)と同時起動する場合は
    # 事前に Get-NetTCPConnection -LocalPort 11156 で空いていることを確認すること。
    $configJson = @'
{
  "port": 11156,
  "forceDirectMode": false
}
'@
    Set-Content -Path $configPath -Value $configJson -Encoding utf8NoBOM
    Write-Host "  Created: ngol-config.json (port 11156)" -ForegroundColor DarkCyan
} else {
    Write-Host "  Kept existing ngol-config.json (not overwritten)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "=== Done: $Dist ===" -ForegroundColor Green
