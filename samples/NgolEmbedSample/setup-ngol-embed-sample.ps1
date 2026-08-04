# setup-ngol-embed-sample.ps1 — ngol-resources/ を組み立てる
#
# 展開済みリリースzipの NGOL/ フォルダから、NGOLリソース
# （Nodes/・WebUI/・Extensions/・ngol-config.json）だけを ngol-resources/ へ配置する。
#
# NGOL 本体の DLL はコピーしない。このサンプルは NGOL を通常のライブラリとして参照しており、
# ビルドが自身の出力へ同梱するため。ここへ別の版の DLL を置くと二重になる。
#
# Usage:
#   .\setup-ngol-embed-sample.ps1 -SourceDir "<展開したNGOL/フォルダのパス>" -OutputDir ".\ngol-resources"

param(
    [Parameter(Mandatory)]
    [string]$SourceDir,

    [string]$OutputDir = (Join-Path $PSScriptRoot "ngol-resources")
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== NgolEmbedSample: ngol-resources 組み立て ===" -ForegroundColor Cyan
Write-Host "  SourceDir : $SourceDir" -ForegroundColor DarkGray
Write-Host "  OutputDir : $OutputDir" -ForegroundColor DarkGray

if (-not (Test-Path $SourceDir)) {
    throw "SourceDir not found: $SourceDir"
}

$coreDll = Join-Path $SourceDir "NodeGraphModLab.Core.dll"
if (-not (Test-Path $coreDll)) {
    throw "NodeGraphModLab.Core.dll not found under SourceDir: $SourceDir`n(SourceDir should be the 'NGOL' folder from an extracted release zip, or an equivalent build output.)"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "`nCopying NGOL resources (no host DLLs)..." -ForegroundColor Yellow
foreach ($assetDir in @("Nodes", "WebUI", "Extensions", "Graphs")) {
    $src = Join-Path $SourceDir $assetDir
    if (Test-Path $src) {
        Copy-Item $src $OutputDir -Recurse -Force
        Write-Host "  Copied: $assetDir/" -ForegroundColor DarkCyan
    }
}

# ngol-config.json はこのサンプル固有の既定値（port 11156）で上書きする。
# SourceDir 側に同名ファイルが無い、または別ホスト向けの値が入っている場合に備えて明示的に配置する。
$configTemplate = Join-Path $PSScriptRoot "ngol-config.json"
$configDest = Join-Path $OutputDir "ngol-config.json"
if (Test-Path $configTemplate) {
    Copy-Item $configTemplate $configDest -Force
    Write-Host "  Copied: ngol-config.json (port 11156 既定)" -ForegroundColor DarkCyan
}

New-Item -ItemType Directory -Path (Join-Path $OutputDir "Nodes\CustomNodes\cs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutputDir "Nodes\CustomNodes\dll") -Force | Out-Null

Write-Host "`n=== 完了: $OutputDir ===" -ForegroundColor Green
Write-Host "  ビルド時に実行ファイルの隣へコピーされます。次のコマンドで起動できます:" -ForegroundColor DarkGray
Write-Host "    dotnet run" -ForegroundColor DarkGray
