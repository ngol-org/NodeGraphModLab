# setup-ngol-embed-sample.ps1 — ngol-plugin/ を組み立てる
#
# 展開済みリリースzipの NGOL/ フォルダ（NodeGraphModLab.Core.dll・NodeAPI.dll・HostLogging.dll・
# 依存DLL・WebUI/・Nodes/Builtin/ 等）を元に、NgolEmbedSample.exe がそのまま起動できる
# ngol-plugin/ を組み立てる。
#
# リポジトリ開発時は、-SourceDir に各プロジェクトのビルド出力
# （NodeGraphModLab.Core / NodeAPI / HostLogging / BuiltinNodes の bin 出力 + WebUI/dist を
# まとめたフォルダ）を指しても良い。
#
# Usage:
#   .\setup-ngol-embed-sample.ps1 -SourceDir "<展開したNGOL/フォルダのパス>" -OutputDir ".\ngol-plugin"

param(
    [Parameter(Mandatory)]
    [string]$SourceDir,

    [string]$OutputDir = (Join-Path $PSScriptRoot "ngol-plugin")
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== NgolEmbedSample: ngol-plugin 組み立て ===" -ForegroundColor Cyan
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

Write-Host "`nCopying SourceDir contents..." -ForegroundColor Yellow
Copy-Item (Join-Path $SourceDir "*") $OutputDir -Recurse -Force
Write-Host "  Copied: $SourceDir -> $OutputDir" -ForegroundColor DarkCyan

# Roslyn(Microsoft.CodeAnalysis.dll等)の言語別サテライトリソース(コンパイルエラーメッセージの
# 多言語化用。機能には無関係)が ngol-plugin 直下に Nodes/ WebUI/ 等と並んで置かれ紛らわしいため、
# 英語(既定・フォルダ無し)・日本語(ja、開発言語)以外を削除する。削除してもホットリロードの
# コンパイルは動作し、エラーメッセージが英語になるだけ。
$satelliteCultures = @("cs", "de", "es", "fr", "it", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant")
foreach ($culture in $satelliteCultures) {
    $cultureDir = Join-Path $OutputDir $culture
    if (Test-Path $cultureDir) {
        Remove-Item $cultureDir -Recurse -Force
    }
}
Write-Host "  Removed: Roslyn satellite resource folders (kept en/ja)" -ForegroundColor DarkCyan

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
Write-Host "  次のコマンドで起動できます:" -ForegroundColor DarkGray
Write-Host "    dotnet run -- `"$OutputDir`"" -ForegroundColor DarkGray
Write-Host "  または publish 済み exe があれば:" -ForegroundColor DarkGray
Write-Host "    NgolEmbedSample.exe `"$OutputDir`"" -ForegroundColor DarkGray
