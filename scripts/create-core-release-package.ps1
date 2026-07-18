<#
.SYNOPSIS
  public Core release zip（NGOL-v{VERSION}.zip）を生成する。

.DESCRIPTION
  release zip の存在理由は「Node ツールチェーン（WebUI: npm run build, mcp: npm run
  build:bundle）不要でランタイム一式（NGOL/）を配布すること」に限定する。
  サンプル（NgolEmbedSample / NgolPluggableSample）は同梱しない — repo の
  samples/ から `git clone` して入手する運用。ネイティブフック拡張機能・逆アセンブル系
  ライブラリも同梱しない。

  -Source は「NodeGraphModLab.Core/ WebUI/ mcp/ 等がトップレベルに揃った」ツリーの
  ルートを指す。省略時はこのスクリプトが置かれているリポジトリ自身（$RepoRoot）を
  対象にするため、このリポジトリを `git clone` した直後にそのまま実行できる。

.PARAMETER Source
  ビルド対象ツリーのルート。省略時はこのスクリプトが置かれているリポジトリのルート。

.PARAMETER SkipBuild
  ビルドをスキップし、既存の Release ビルド成果物を使う。

.PARAMETER OutputDir
  zip 出力先（既定: <Source>\release\public）。

.EXAMPLE
  .\scripts\create-core-release-package.ps1 -SkipBuild
.EXAMPLE
  .\scripts\create-core-release-package.ps1 -Source "D:\work\ngol-public" -OutputDir "D:\work\ngol-public\release"
#>

param(
    [string]$Source = "",
    [switch]$SkipBuild,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = $RepoRoot }
$Source = (Resolve-Path $Source).Path

if (-not $OutputDir) { $OutputDir = Join-Path $Source "release\public" }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# バージョン取得: NodeGraphModLab.Core.csproj の Version を参照する。
# (当初案の plugin/NodeGraphModLab.csproj は public 非同期のため参照不可。設計 §6.4 改訂注記)
$coreCsprojPath = Join-Path $Source "NodeGraphModLab.Core\NodeGraphModLab.Core.csproj"
if (-not (Test-Path $coreCsprojPath)) { throw "NodeGraphModLab.Core.csproj not found: $coreCsprojPath" }
[xml]$coreCsproj = Get-Content $coreCsprojPath
$version = $coreCsproj.Project.PropertyGroup.Version | Where-Object { $_ -ne $null } | Select-Object -First 1
if (-not $version) { $version = "0.0.0" }

Write-Host "`n=== create-core-release-package.ps1 ===" -ForegroundColor Cyan
Write-Host "  Version   : $version" -ForegroundColor DarkGray
Write-Host "  Source    : $Source" -ForegroundColor DarkGray
Write-Host "  OutputDir : $OutputDir" -ForegroundColor DarkGray

# ===== Step 1: WebUI ビルド =====
if (-not $SkipBuild) {
    Write-Host "`n[1/4] WebUI ビルド中..." -ForegroundColor Yellow
    Push-Location (Join-Path $Source "WebUI")
    try {
        if (-not (Test-Path "node_modules")) {
            npm ci 2>&1
            if ($LASTEXITCODE -ne 0) { throw "WebUI 依存関係インストール失敗" }
        }
        npm run build 2>&1
        if ($LASTEXITCODE -ne 0) { throw "WebUI ビルド失敗" }
        Write-Host "  OK: WebUI ビルド成功" -ForegroundColor Green
    } finally { Pop-Location }
} else {
    Write-Host "[1/4] WebUI ビルドをスキップ" -ForegroundColor Gray
}

# ===== Step 2: .NET ビルド（Release）=====
# NodeGraphModLab.Core.sln（Core/NodeAPI/HostLogging/BuiltinNodesの4プロジェクトのみ。plugin/Testsは含まない）を
# 一括ビルドする。HostLogging・サンプル群はCoreへProjectReferenceで参照しているため、MSBuildが依存順序を
# 保証し、1回の実行で成功する（旧: HintPath参照だったため個別ループでビルド順を手動制御していた）。
if (-not $SkipBuild) {
    Write-Host "`n[2/4] .NET ビルド (Release)..." -ForegroundColor Yellow
    $slnPath = Join-Path $Source "NodeGraphModLab.Core.sln"
    dotnet build $slnPath --configuration Release 2>&1
    if ($LASTEXITCODE -ne 0) { throw ".NET ビルド失敗: $slnPath" }
    Write-Host "  OK: .NET ビルド成功" -ForegroundColor Green
} else {
    Write-Host "[2/4] .NET ビルドをスキップ" -ForegroundColor Gray
}

# ===== Step 3: MCP バンドルビルド =====
if (-not $SkipBuild) {
    Write-Host "`n[3/4] MCP バンドルビルド..." -ForegroundColor Yellow
    Push-Location (Join-Path $Source "mcp")
    try {
        if (-not (Test-Path "node_modules")) {
            npm ci 2>&1
            if ($LASTEXITCODE -ne 0) { throw "MCP 依存関係インストール失敗" }
        }
        npm run build:bundle 2>&1
        if ($LASTEXITCODE -ne 0) { throw "MCP バンドルビルド失敗" }
        Write-Host "  OK: MCP バンドルビルド成功" -ForegroundColor Green
    } finally { Pop-Location }
} else {
    Write-Host "[3/4] MCP バンドルビルドをスキップ" -ForegroundColor Gray
}

# ===== Step 4: ステージング & zip 生成 =====
Write-Host "`n[4/4] パッケージ生成..." -ForegroundColor Yellow

$stagingRoot = Join-Path $OutputDir "staging"
$ngolStaging = Join-Path $stagingRoot "NGOL"
if (Test-Path $stagingRoot) { Remove-Item $stagingRoot -Recurse -Force }
New-Item -ItemType Directory -Path $ngolStaging -Force | Out-Null

# Core.dll / NodeAPI.dll / Roslyn / LiteDB（net6.0 出力。Iced.dll は同梱しない — 設計 §4.1.2(b)）
$coreNet6 = Join-Path $Source "NodeGraphModLab.Core\bin\Release\net6.0"
foreach ($dll in @("NodeGraphModLab.Core.dll", "NodeGraphModLab.NodeAPI.dll",
                    "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll", "LiteDB.dll")) {
    $src = Join-Path $coreNet6 $dll
    if (Test-Path $src) {
        Copy-Item $src $ngolStaging -Force
        Write-Host "    Copied: $dll" -ForegroundColor DarkCyan
    } else { Write-Warning "Not found: $src" }
}

# HostLogging.dll
$hostLoggingSrc = Join-Path $Source "NodeGraphModLab.HostLogging\bin\Release\net6.0\NodeGraphModLab.HostLogging.dll"
if (Test-Path $hostLoggingSrc) {
    Copy-Item $hostLoggingSrc $ngolStaging -Force
    Write-Host "    Copied: NodeGraphModLab.HostLogging.dll" -ForegroundColor DarkCyan
} else { Write-Warning "Not found: $hostLoggingSrc" }

# BuiltinNodes.dll（汎用ノードのみ。ホスト固有ノードは samples/CustomNodes/ 側の private サンプル）→ Nodes/Builtin/
$builtinSrc = Join-Path $Source "NodeGraphModLab.BuiltinNodes\bin\Release\netstandard2.0\NodeGraphModLab.BuiltinNodes.dll"
$builtinDest = Join-Path $ngolStaging "Nodes\Builtin"
New-Item -ItemType Directory -Path $builtinDest -Force | Out-Null
if (Test-Path $builtinSrc) {
    Copy-Item $builtinSrc $builtinDest -Force
    Write-Host "    Copied: NodeGraphModLab.BuiltinNodes.dll -> Nodes/Builtin/" -ForegroundColor DarkCyan
} else { Write-Warning "BuiltinNodes DLL not found: $builtinSrc" }

New-Item -ItemType Directory -Path (Join-Path $ngolStaging "Nodes\CustomNodes\cs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ngolStaging "Nodes\CustomNodes\dll") -Force | Out-Null

# WebUI
$webUiDist = Join-Path $Source "WebUI\dist"
$webUiDest = Join-Path $ngolStaging "WebUI"
New-Item -ItemType Directory -Path $webUiDest -Force | Out-Null
if (Test-Path $webUiDist) {
    Copy-Item "$webUiDist\*" $webUiDest -Recurse -Force
    Write-Host "    Copied: WebUI/" -ForegroundColor DarkCyan
} else { Write-Warning "WebUI dist not found: $webUiDist" }

# mcp（public 版: bundle.js -> dist/index.js、docs/・README.md・mcp.json.example は
# public repo 側で既に .public サフィックスがリネームされている前提 — 設計 §4.1.3）
$mcpBundleSrc = Join-Path $Source "mcp\dist\bundle.js"
$mcpDocsSrc   = Join-Path $Source "mcp\docs"
$mcpGuideSrc  = Join-Path $Source "mcp\README.md"
$mcpJsonExSrc = Join-Path $Source "mcp\mcp.json.example"
$mcpRootDest  = Join-Path $ngolStaging "mcp"
$mcpDistDest  = Join-Path $ngolStaging "mcp\dist"
$mcpDocsDest  = Join-Path $ngolStaging "mcp\docs"
New-Item -ItemType Directory -Path $mcpDistDest -Force | Out-Null
if (Test-Path $mcpBundleSrc) {
    Copy-Item $mcpBundleSrc (Join-Path $mcpDistDest "index.js") -Force
    Write-Host "    Copied: mcp/dist/bundle.js -> mcp/dist/index.js" -ForegroundColor DarkCyan
} else { Write-Warning "MCP bundle not found: $mcpBundleSrc" }
if (Test-Path $mcpDocsSrc) {
    Copy-Item $mcpDocsSrc $mcpDocsDest -Recurse -Force
    Write-Host "    Copied: mcp/docs/" -ForegroundColor DarkCyan
} else { Write-Warning "MCP docs not found: $mcpDocsSrc" }
foreach ($f in @($mcpGuideSrc, $mcpJsonExSrc)) {
    if (Test-Path $f) {
        Copy-Item $f $mcpRootDest -Force
        Write-Host "    Copied: mcp/$(Split-Path $f -Leaf)" -ForegroundColor DarkCyan
    } else { Write-Warning "Not found: $f" }
}
Set-Content (Join-Path $mcpRootDest "package.json") -Value '{"type":"module"}' -Encoding UTF8 -NoNewline
Write-Host "    Created: mcp/package.json (type:module)" -ForegroundColor DarkCyan

# README.md / CUSTOM_NODE_GUIDE.md / LICENSE
foreach ($doc in @("README.md", "CUSTOM_NODE_GUIDE.md", "LICENSE")) {
    $src = Join-Path $Source $doc
    if (Test-Path $src) {
        Copy-Item $src $ngolStaging -Force
        Write-Host "    Copied: $doc" -ForegroundColor DarkCyan
    } else { Write-Warning "Not found (Phase 2 対応待ちの可能性あり): $src" }
}

# zip 生成
$zipPath = Join-Path $OutputDir "NGOL-v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $ngolStaging -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "  OK: $zipPath" -ForegroundColor Green

# staging/NGOL/ は削除せず残す。zip を経由せずそのまま -SourceDir に渡せる
# （setup-ngol-embed-sample.ps1 / setup-ngol-pluggable-sample.ps1 参照）。

Write-Host "`n=== 完了 ===" -ForegroundColor Green
Write-Host "  zip        : $zipPath" -ForegroundColor Cyan
Write-Host "  展開済み   : $ngolStaging" -ForegroundColor Cyan
Write-Host "  （zipを展開する代わりに、上記の展開済みフォルダを直接 -SourceDir に渡してもよい）" -ForegroundColor DarkGray
