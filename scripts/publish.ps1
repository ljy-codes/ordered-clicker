$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\OrderedClicker\OrderedClicker.csproj"
$outputDirectory = Join-Path $projectRoot "publish\win-x64"
$rootPrefix = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
$resolvedOutput = [System.IO.Path]::GetFullPath($outputDirectory)

if (-not $resolvedOutput.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录不在项目目录内：$resolvedOutput"
}

if (Test-Path -LiteralPath $resolvedOutput) {
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
    -o $resolvedOutput `
    '-m:1'

if ($LASTEXITCODE -ne 0) {
    throw "发布失败，退出码：$LASTEXITCODE"
}

$executable = Join-Path $resolvedOutput "OrderedClicker.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "发布完成但未找到 EXE：$executable"
}

$unexpectedRuntimeFiles = Get-ChildItem -LiteralPath $resolvedOutput -File |
    Where-Object { $_.Extension -in ".dll", ".runtimeconfig.json", ".deps.json" }
if ($unexpectedRuntimeFiles.Count -gt 0) {
    throw "发布目录仍包含运行时依赖文件：$($unexpectedRuntimeFiles.Name -join ', ')"
}

Write-Host "发布完成：$executable"
