[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "1.1.0"
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\OrderedClicker\OrderedClicker.csproj"
$outputDirectory = Join-Path $projectRoot "publish\win-x64"
. (Join-Path $PSScriptRoot "path-safety.ps1")

$resolvedOutput = Assert-SafeRecursivePath -Path $outputDirectory -ParentPath $projectRoot

if (Test-Path -LiteralPath $resolvedOutput) {
    Assert-SafeRecursivePath -Path $resolvedOutput -ParentPath $projectRoot | Out-Null
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

& (Join-Path $PSScriptRoot "dotnet.ps1") publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    '-p:PublishSingleFile=true' `
    '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:EnableCompressionInSingleFile=true' `
    '-p:DebugType=embedded' `
    '-p:DebugSymbols=false' `
    "-p:Version=$Version" `
    "-p:AssemblyVersion=${Version}.0" `
    "-p:FileVersion=${Version}.0" `
    "-p:InformationalVersion=$Version" `
    -o $resolvedOutput `
    '-m:1'

if ($LASTEXITCODE -ne 0) {
    throw "发布失败，退出码：$LASTEXITCODE"
}

$executable = Join-Path $resolvedOutput "OrderedClicker.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "发布完成但未找到 EXE：$executable"
}

$unexpectedRuntimeFiles = @(
    Get-ChildItem -LiteralPath $resolvedOutput -File |
        Where-Object { $_.Extension -in ".dll", ".runtimeconfig.json", ".deps.json" }
)
if ($unexpectedRuntimeFiles.Count -gt 0) {
    throw "发布目录仍包含运行时依赖文件：$($unexpectedRuntimeFiles.Name -join ', ')"
}

Write-Host "发布完成：$executable"
