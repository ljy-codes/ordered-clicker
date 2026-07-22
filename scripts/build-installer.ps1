[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "1.2.0",

    [string]$ProductDirectory = "",

    [string]$GuideSourceDirectory = "",

    [string]$InnoCompiler = "",

    [switch]$SkipTests
)

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "构建安装版需要 PowerShell 7 或更高版本。请使用 pwsh 运行本脚本。"
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $projectRoot "publish"
$publishDirectory = Join-Path $publishRoot "win-x64"
$packageDirectory = Join-Path $publishRoot "packages"
$portableStagingDirectory = Join-Path $publishRoot "portable-staging"
$productStagingDirectory = Join-Path $publishRoot "product-staging"
$installerScript = Join-Path $projectRoot "installer\OrderedClicker.iss"
$portableFileName = "ordered-clicker-portable-v$Version.zip"
$installerFileName = "ordered-clicker-setup-v$Version.exe"
$portablePath = Join-Path $packageDirectory $portableFileName
$installerPath = Join-Path $packageDirectory $installerFileName

. (Join-Path $PSScriptRoot "path-safety.ps1")

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

foreach ($directory in @(
    $packageDirectory,
    $portableStagingDirectory,
    $productStagingDirectory
)) {
    Assert-SafeRecursivePath -Path $directory -ParentPath $projectRoot | Out-Null
}

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

$guideMappings = [ordered]@{
    "有序连点器-使用说明.pdf" = "有序连点器-使用说明.pdf"
    "有序连点器-使用说明.html" = "有序连点器-使用说明.html"
    "有序连点器-完整操作教程.mp4" = "有序连点器-视频演示.mp4"
}
$resolvedGuideFiles = [System.Collections.Generic.List[object]]::new()
foreach ($sourceName in $guideMappings.Keys) {
    $source = Join-Path $resolvedGuideSourceDirectory $sourceName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "缺少指导文件：$source。视频属于生成产物，请先生成完整教程或使用 -GuideSourceDirectory 指定成品目录。"
    }

    $resolvedGuideFiles.Add([pscustomobject]@{
        Source = $source
        DestinationName = $guideMappings[$sourceName]
    })
}

$resolvedInnoCompiler = Find-InnoCompiler -RequestedPath $InnoCompiler

if (-not $SkipTests) {
    Invoke-Checked -Description "运行 .NET 自动化测试" -Command {
        & (Join-Path $PSScriptRoot "dotnet.ps1") run `
            --project (Join-Path $projectRoot "tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj") `
            -c Release
    }

    Invoke-Checked -Description "运行安装器契约测试" -Command {
        & (Join-Path $projectRoot "tests\Installer.Tests.ps1")
    }
}

Invoke-Checked -Description "发布 Windows x64 自包含应用" -Command {
    & (Join-Path $PSScriptRoot "publish.ps1") -Version $Version
}

foreach ($directory in @(
    $packageDirectory,
    $portableStagingDirectory,
    $productStagingDirectory
)) {
    $safeDirectory = Assert-SafeRecursivePath -Path $directory -ParentPath $projectRoot
    if (Test-Path -LiteralPath $safeDirectory) {
        Assert-SafeRecursivePath -Path $safeDirectory -ParentPath $projectRoot | Out-Null
        Remove-Item -LiteralPath $safeDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $safeDirectory | Out-Null
}

$publishedExecutable = Join-Path $publishDirectory "OrderedClicker.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "发布完成但未找到应用程序：$publishedExecutable"
}

$publishedVersion = (Get-Item -LiteralPath $publishedExecutable).VersionInfo
if (
    $publishedVersion.FileVersion -ne "$Version.0" -or
    $publishedVersion.ProductVersion -ne $Version
) {
    throw (
        "应用版本不一致。期望 FileVersion=$Version.0、ProductVersion=$Version；" +
        "实际 FileVersion=$($publishedVersion.FileVersion)、" +
        "ProductVersion=$($publishedVersion.ProductVersion)"
    )
}

$portableExecutable = Join-Path $portableStagingDirectory "有序连点器.exe"
Copy-Item -LiteralPath $publishedExecutable -Destination $portableExecutable -Force
Compress-Archive -LiteralPath $portableExecutable -DestinationPath $portablePath -CompressionLevel Optimal

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

$stagedProductFiles = [System.Collections.Generic.List[string]]::new()
foreach ($packagePath in @($installerPath, $portablePath)) {
    $destination = Join-Path $productStagingDirectory ([System.IO.Path]::GetFileName($packagePath))
    Copy-Item -LiteralPath $packagePath -Destination $destination -Force
    $stagedProductFiles.Add($destination)
}

foreach ($guideFile in $resolvedGuideFiles) {
    $destination = Join-Path $productStagingDirectory $guideFile.DestinationName
    Copy-Item -LiteralPath $guideFile.Source -Destination $destination -Force
    $stagedProductFiles.Add($destination)
}

$stagedChecksumPath = Join-Path $productStagingDirectory "SHA256SUMS.txt"
$checksumLines = foreach ($file in $stagedProductFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($file)
}
[System.IO.File]::WriteAllLines(
    $stagedChecksumPath,
    $checksumLines,
    [System.Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force -Path $resolvedProductDirectory | Out-Null
$deliveryStagingDirectory = Join-Path `
    $resolvedProductDirectory `
    ".ordered-clicker-staging-$([Guid]::NewGuid().ToString('N'))"
$safeDeliveryStagingDirectory = Assert-SafeRecursivePath `
    -Path $deliveryStagingDirectory `
    -ParentPath $resolvedProductDirectory
New-Item -ItemType Directory -Force -Path $safeDeliveryStagingDirectory | Out-Null

$ownedProductPatterns = @(
    "ordered-clicker-setup-v*.exe",
    "ordered-clicker-portable-v*.zip",
    "有序连点器-使用说明.pdf",
    "有序连点器-使用说明.html",
    "有序连点器-视频演示.mp4",
    "SHA256SUMS.txt"
)
$destinationChecksumPath = Join-Path $resolvedProductDirectory "SHA256SUMS.txt"
try {
    $deliveryFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($stagedFile in $stagedProductFiles) {
        $destination = Join-Path `
            $safeDeliveryStagingDirectory `
            ([System.IO.Path]::GetFileName($stagedFile))
        Copy-Item -LiteralPath $stagedFile -Destination $destination -Force
        $deliveryFiles.Add($destination)
    }

    $deliveryChecksumPath = Join-Path $safeDeliveryStagingDirectory "SHA256SUMS.txt"
    Copy-Item -LiteralPath $stagedChecksumPath -Destination $deliveryChecksumPath -Force

    foreach ($index in 0..($stagedProductFiles.Count - 1)) {
        $sourceHash = (Get-FileHash -LiteralPath $stagedProductFiles[$index] -Algorithm SHA256).Hash
        $deliveryHash = (Get-FileHash -LiteralPath $deliveryFiles[$index] -Algorithm SHA256).Hash
        if ($sourceHash -ne $deliveryHash) {
            throw "产品文件暂存校验失败：$($deliveryFiles[$index])"
        }
    }

    foreach ($deliveryFile in $deliveryFiles) {
        $destination = Join-Path `
            $resolvedProductDirectory `
            ([System.IO.Path]::GetFileName($deliveryFile))
        Move-Item -LiteralPath $deliveryFile -Destination $destination -Force
    }
    Move-Item `
        -LiteralPath $deliveryChecksumPath `
        -Destination $destinationChecksumPath `
        -Force

    $currentProductNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $stagedProductFiles | ForEach-Object {
        [System.IO.Path]::GetFileName($_)
    }) {
        $currentProductNames.Add($name) | Out-Null
    }
    $currentProductNames.Add("SHA256SUMS.txt") | Out-Null

    foreach ($pattern in $ownedProductPatterns) {
        Get-ChildItem -LiteralPath $resolvedProductDirectory -File -Filter $pattern |
            Where-Object { -not $currentProductNames.Contains($_.Name) } |
            Remove-Item -Force
    }
}
finally {
    if (Test-Path -LiteralPath $safeDeliveryStagingDirectory) {
        Assert-SafeRecursivePath `
            -Path $safeDeliveryStagingDirectory `
            -ParentPath $resolvedProductDirectory | Out-Null
        Remove-Item -LiteralPath $safeDeliveryStagingDirectory -Recurse -Force
    }
}

Write-Host ""
Write-Host "安装版构建完成：" -ForegroundColor Green
Write-Host "  安装包：$(Join-Path $resolvedProductDirectory $installerFileName)"
Write-Host "  便携版：$(Join-Path $resolvedProductDirectory $portableFileName)"
Write-Host "  校验值：$destinationChecksumPath"
