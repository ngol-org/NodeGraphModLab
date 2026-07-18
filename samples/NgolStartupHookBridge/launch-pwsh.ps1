# launch-pwsh.ps1 - NgolStartupHookBridge を pwsh.exe (PowerShell 7+) へ注入して起動する
#
# DOTNET_STARTUP_HOOKS は .NET Core 3.0+ の機能のため、.NET Core/.NET製の pwsh.exe
# (PowerShell 7+) には注入できるが、.NET Framework製の旧来の powershell.exe
# (Windows PowerShell 5.1) には効かない。
#
# 事前準備:
#   dotnet build .\NgolStartupHookBridge.csproj -c Debug
#   .\prepare-ngol-plugin-dir.ps1
#
# Usage:
#   .\launch-pwsh.ps1
#   .\launch-pwsh.ps1 -Configuration Release

param([string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"
$SampleRoot = $PSScriptRoot

$bridgeDll = Join-Path $SampleRoot "bin\$Configuration\net6.0\NgolStartupHookBridge.dll"
$pluginDir = Join-Path $SampleRoot "ngol-plugin"

$pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
if (-not $pwshCmd) { throw "pwsh.exe (PowerShell 7+) が見つかりません。https://aka.ms/powershell からインストールしてください。" }
$pwshExe = $pwshCmd.Source

if (-not (Test-Path $bridgeDll)) { throw "Bridge DLL not found: $bridgeDll (先に dotnet build してください)" }
if (-not (Test-Path $pluginDir)) { throw "ngol-plugin not found: $pluginDir (先に .\prepare-ngol-plugin-dir.ps1 を実行してください)" }

Write-Host ""
Write-Host "=== NgolStartupHookBridge -> pwsh.exe (PowerShell 7+) ===" -ForegroundColor Cyan
Write-Host "  DOTNET_STARTUP_HOOKS   : $bridgeDll" -ForegroundColor DarkGray
Write-Host "  NGOL_BRIDGE_PLUGIN_DIR : $pluginDir" -ForegroundColor DarkGray
Write-Host "  Target                 : $pwshExe" -ForegroundColor DarkGray
Write-Host ""

$env:DOTNET_STARTUP_HOOKS = $bridgeDll
$env:NGOL_BRIDGE_PLUGIN_DIR = $pluginDir

Start-Process -FilePath $pwshExe -ArgumentList "-NoExit", "-NoLogo" -PassThru

Remove-Item Env:\DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue
Remove-Item Env:\NGOL_BRIDGE_PLUGIN_DIR -ErrorAction SilentlyContinue
