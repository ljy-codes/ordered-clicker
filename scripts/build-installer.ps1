[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "1.0.1",

    [string]$ProductDirectory = "",

    [string]$GuideSourceDirectory = "",

    [string]$InnoCompiler = "",

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $projectRoot "publish"
$publishDirectory = Join-Path $publishRoot "win-x64"
$packageDirectory = Join-Path $publishRoot "packages"
$portableStagingDirectory = Join-Path $publishRoot "portable-staging"
$installerScript = Join-Path $projectRoot "installer\OrderedClicker.iss"
$portableFileName = "ordered-clicker-portable-v$Version.zip"
$installerFileName = "ordered-clicker-setup-v$Version.exe"
$portablePath = Join-Path $packageDirectory $portableFileName
$installerPath = Join-Path $packageDirectory $installerFileName

function Get-FullPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Assert-PathWithin {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ParentPath
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理项目发布目录以外的路径：$resolvedPath"
    }
}

function Find-InnoCompiler {
    param(
        [string]$RequestedPath
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add((Get-FullPath -Path $RequestedPath -BasePath $projectRoot))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER)) {
        $candidates.Add((Get-FullPath -Path $env:INNO_SETUP_COMPILER -BasePath $projectRoot))
    }

    $candidates.Add((Join-Path $projectRoot ".tools\inno-setup\ISCC.exe"))
    $candidates.Add((Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"))
    $candidates.Add((Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"))
    if (${env:ProgramFiles(x86)}) {
        $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"))
    }
    $candidates.Add((Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"))
    $candidates.Add((Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"))
    if (${env:ProgramFiles(x86)}) {
        $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"))
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "未找到 Inno Setup 编译器 ISCC.exe。请安装 Inno Setup 7，或使用 -InnoCompiler 指定路径。"
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description 失败，退出码：$LASTEXITCODE"
    }
}

Assert-PathWithin -Path $packageDirectory -ParentPath $publishRoot
Assert-PathWithin -Path $portableStagingDirectory -ParentPath $publishRoot

if ([string]::IsNullOrWhiteSpace($ProductDirectory)) {
    $resolvedProductDirectory = Join-Path $publishRoot "product-v$Version"
}
else {
    $resolvedProductDirectory = Get-FullPath -Path $ProductDirectory -BasePath $projectRoot
}

if ([string]::IsNullOrWhiteSpace($GuideSourceDirectory)) {
    $resolvedGuideSourceDirectory = Join-Path $projectRoot "操作指导"
}
else {
    $resolvedGuideSourceDirectory = Get-FullPath -Path $GuideSourceDirectory -BasePath $projectRoot
}

if (-not $SkipTests) {
    Invoke-Checked -Description "运行 .NET 自动化测试" -Command {
        & (Join-Path $PSScriptRoot "dotnet.ps1") run `
            --project (Join-Path $projectRoot "tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj") `
            -c Release
    }

    Invoke-Checked -Description "运行安装器契约测试" -Command {
        & (Join-Path $PSHOME "pwsh.exe") -NoProfile `
            -File (Join-Path $projectRoot "tests\Installer.Tests.ps1")
    }
}

Invoke-Checked -Description "发布 Windows x64 自包含应用" -Command {
    & (Join-Path $PSScriptRoot "publish.ps1")
}

foreach ($directory in @($packageDirectory, $portableStagingDirectory)) {
    Assert-PathWithin -Path $directory -ParentPath $publishRoot
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$publishedExecutable = Join-Path $publishDirectory "OrderedClicker.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "发布完成但未找到应用程序：$publishedExecutable"
}

$portableExecutable = Join-Path $portableStagingDirectory "有序连点器.exe"
Copy-Item -LiteralPath $publishedExecutable -Destination $portableExecutable -Force
Compress-Archive -LiteralPath $portableExecutable -DestinationPath $portablePath -CompressionLevel Optimal

$resolvedInnoCompiler = Find-InnoCompiler -RequestedPath $InnoCompiler
Invoke-Checked -Description "编译 Windows 安装程序" -Command {
    & $resolvedInnoCompiler `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publishDirectory" `
        "/DOutputDir=$packageDirectory" `
        $installerScript
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Inno Setup 编译完成但未找到安装包：$installerPath"
}

New-Item -ItemType Directory -Force -Path $resolvedProductDirectory | Out-Null

$guideMappings = [ordered]@{
    "有序连点器-操作指导PRD.pdf" = "有序连点器-使用说明.pdf"
    "有序连点器-操作指导PRD.html" = "有序连点器-使用说明.html"
    "有序连点器-完整操作教程.mp4" = "有序连点器-视频演示.mp4"
}

$productFiles = [System.Collections.Generic.List[string]]::new()
foreach ($packagePath in @($installerPath, $portablePath)) {
    $destination = Join-Path $resolvedProductDirectory ([System.IO.Path]::GetFileName($packagePath))
    Copy-Item -LiteralPath $packagePath -Destination $destination -Force
    $productFiles.Add($destination)
}

foreach ($sourceName in $guideMappings.Keys) {
    $source = Join-Path $resolvedGuideSourceDirectory $sourceName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "缺少指导文件：$source"
    }

    $destination = Join-Path $resolvedProductDirectory $guideMappings[$sourceName]
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $productFiles.Add($destination)
}

$checksumPath = Join-Path $resolvedProductDirectory "SHA256SUMS.txt"
$checksumLines = foreach ($file in $productFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($file)
}
[System.IO.File]::WriteAllLines(
    $checksumPath,
    $checksumLines,
    [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "安装版构建完成：" -ForegroundColor Green
Write-Host "  安装包：$(Join-Path $resolvedProductDirectory $installerFileName)"
Write-Host "  便携版：$(Join-Path $resolvedProductDirectory $portableFileName)"
Write-Host "  校验值：$checksumPath"
