<#
.SYNOPSIS
  NodeGraphModLab.Tests（NUnit 単体テスト）を実行する。

.DESCRIPTION
  NodeGraphModLab.Tests.csproj は NodeGraphModLab.Core.sln に未登録のため、
  `dotnet test` にプロジェクトパスを直接渡すための薄いラッパー。

.PARAMETER Filter
  dotnet test --filter に渡すフィルタ式（例: "ClassName=ExecuteGraphHandlerTests"）。省略可。

.EXAMPLE
  .\scripts\run-core-tests.ps1
.EXAMPLE
  .\scripts\run-core-tests.ps1 -Filter "ClassName=ExecuteGraphHandlerTests"
#>

param(
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$TestsProject = Join-Path $RepoRoot "NodeGraphModLab.Tests"

Write-Host "`n=== run-core-tests.ps1 ===" -ForegroundColor Cyan
Write-Host "  Project : $TestsProject" -ForegroundColor DarkGray

if ($Filter) {
    dotnet test $TestsProject --filter $Filter
} else {
    dotnet test $TestsProject
}
