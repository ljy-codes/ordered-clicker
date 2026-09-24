[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot "tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj"
$arguments = @(
    "test",
    $testProject,
    "--configuration", $Configuration
)
if ($NoRestore) {
    $arguments += "--no-restore"
}

$output = & dotnet @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }

if ($exitCode -ne 0) {
    throw "测试失败，退出码：$exitCode"
}

$summary = $output | Select-String -Pattern "^\s*\d+/\d+ tests passed\s*$"
if ($null -eq $summary) {
    throw "测试程序未输出通过摘要，拒绝将本次执行视为成功。"
}
