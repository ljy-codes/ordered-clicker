[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "2.0.0",

    [string]$ProductDirectory = "",

    [string]$GuideSourceDirectory = "",

    [string]$InnoCompiler = "",

    [switch]$Release,

    [string]$SigningCertificatePath = "",

    [string]$TimestampUrl = "http://timestamp.digicert.com",

    [string]$SignTool = "",

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
$portablePublishDirectory = Join-Path $publishRoot "win-x64-portable"
$packageDirectory = Join-Path $publishRoot "packages"
$portableStagingDirectory = Join-Path $publishRoot "portable-staging"
$productStagingDirectory = Join-Path $publishRoot "product-staging"
$installerScript = Join-Path $projectRoot "installer\OrderedClicker.iss"
$portableFileName = "ordered-clicker-portable-v$Version.zip"
$installerFileName = "ordered-clicker-setup-v$Version.exe"
$portablePath = Join-Path $packageDirectory $portableFileName
$installerPath = Join-Path $packageDirectory $installerFileName
$portableDeliveryFileName = "有序连点器-免安装.exe"

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
    $resolvedProductDirectory = Join-Path $projectRoot "有序连点器"
}
else {
    $resolvedProductDirectory = Get-FullPath -Path $ProductDirectory -BasePath $projectRoot
}

function Find-SignTool {
    param(
        [string]$RequestedPath
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add((Get-FullPath -Path $RequestedPath -BasePath $projectRoot))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:SIGNTOOL_PATH)) {
        $candidates.Add((Get-FullPath -Path $env:SIGNTOOL_PATH -BasePath $projectRoot))
    }

    $windowsKits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $windowsKits -PathType Container) {
        Get-ChildItem -LiteralPath $windowsKits -Directory |
            Sort-Object Name -Descending |
            ForEach-Object {
                $candidates.Add((Join-Path $_.FullName "x64\signtool.exe"))
            }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "未找到 signtool.exe。请安装 Windows SDK，或使用 -SignTool 指定路径。"
}

function Get-SigningPassword {
    if (-not [string]::IsNullOrWhiteSpace($env:ORDERED_CLICKER_SIGNING_PASSWORD)) {
        try {
            return ConvertTo-SecureString `
                $env:ORDERED_CLICKER_SIGNING_PASSWORD `
                -AsPlainText `
                -Force
        }
        finally {
            Remove-Item Env:\ORDERED_CLICKER_SIGNING_PASSWORD -ErrorAction SilentlyContinue
        }
    }

    return Read-Host "请输入代码签名证书密码" -AsSecureString
}

function Import-SigningCertificate {
    param(
        [Parameter(Mandatory)]
        [string]$CertificatePath,

        [Parameter(Mandatory)]
        [securestring]$Password
    )

    $existingThumbprints = @(
        Get-ChildItem -Path Cert:\CurrentUser\My |
            ForEach-Object { $_.Thumbprint }
    )
    $certificate = Import-PfxCertificate `
        -FilePath $CertificatePath `
        -CertStoreLocation Cert:\CurrentUser\My `
        -Password $Password `
        -Exportable:$false
    if ($null -eq $certificate -or [string]::IsNullOrWhiteSpace($certificate.Thumbprint)) {
        throw "代码签名证书导入失败。"
    }

    return [pscustomobject]@{
        Thumbprint = $certificate.Thumbprint
        RemoveAfterBuild = $certificate.Thumbprint -notin $existingThumbprints
    }
}

function Invoke-CodeSigning {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$SignToolPath,

        [Parameter(Mandatory)]
        [string]$CertificateThumbprint
    )

    Invoke-Checked -Description "签名 $([System.IO.Path]::GetFileName($Path))" -Command {
        & $SignToolPath sign `
            /sha1 $CertificateThumbprint `
            /s My `
            /fd SHA256 `
            /tr $TimestampUrl `
            /td SHA256 `
            $Path
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "签名验证失败：$Path，状态：$($signature.Status)"
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "签名缺少可信时间戳：$Path"
    }
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
}
$resolvedGuideFiles = [System.Collections.Generic.List[object]]::new()
foreach ($sourceName in $guideMappings.Keys) {
    $source = Join-Path $resolvedGuideSourceDirectory $sourceName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "缺少指导文件：$source。请生成 PDF/HTML 使用说明，或使用 -GuideSourceDirectory 指定成品目录。"
    }

    $resolvedGuideFiles.Add([pscustomobject]@{
        Source = $source
        DestinationName = $guideMappings[$sourceName]
    })
}

$resolvedInnoCompiler = Find-InnoCompiler -RequestedPath $InnoCompiler
$resolvedSignTool = $null
$resolvedSigningCertificate = $null
$signingPassword = $null
if ($Release) {
    if ([string]::IsNullOrWhiteSpace($SigningCertificatePath)) {
        throw "Release 构建必须使用 -SigningCertificatePath 指定代码签名证书。"
    }

    $resolvedSigningCertificate = Get-FullPath `
        -Path $SigningCertificatePath `
        -BasePath $projectRoot
    if (-not (Test-Path -LiteralPath $resolvedSigningCertificate -PathType Leaf)) {
        throw "代码签名证书不存在：$resolvedSigningCertificate"
    }

    $resolvedSignTool = Find-SignTool -RequestedPath $SignTool
    $signingPassword = Get-SigningPassword
}

$signingCertificateState = $null
if ($Release) {
    $signingCertificateState = Import-SigningCertificate `
        -CertificatePath $resolvedSigningCertificate `
        -Password $signingPassword
}

try {
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

Invoke-Checked -Description "发布 Windows x64 免安装应用" -Command {
    & (Join-Path $PSScriptRoot "publish.ps1") -Version $Version -Portable
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

$publishedPortableExecutable = Join-Path $portablePublishDirectory "OrderedClicker.exe"
if (-not (Test-Path -LiteralPath $publishedPortableExecutable -PathType Leaf)) {
    throw "发布完成但未找到免安装应用程序：$publishedPortableExecutable"
}
$publishedPortableVersion = (Get-Item -LiteralPath $publishedPortableExecutable).VersionInfo
if (
    $publishedPortableVersion.FileVersion -ne "$Version.0" -or
    $publishedPortableVersion.ProductVersion -ne $Version
) {
    throw "免安装应用版本不一致。"
}

if ($Release) {
    Invoke-CodeSigning `
        -Path $publishedExecutable `
        -SignToolPath $resolvedSignTool `
        -CertificateThumbprint $signingCertificateState.Thumbprint
    Invoke-CodeSigning `
        -Path $publishedPortableExecutable `
        -SignToolPath $resolvedSignTool `
        -CertificateThumbprint $signingCertificateState.Thumbprint
}

$portableExecutable = Join-Path $portableStagingDirectory $portableDeliveryFileName
Copy-Item -LiteralPath $publishedPortableExecutable -Destination $portableExecutable -Force
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

if ($Release) {
    Invoke-CodeSigning `
        -Path $installerPath `
        -SignToolPath $resolvedSignTool `
        -CertificateThumbprint $signingCertificateState.Thumbprint
}

$stagedProductFiles = [System.Collections.Generic.List[string]]::new()
$stagedInstaller = Join-Path $productStagingDirectory $installerFileName
Copy-Item -LiteralPath $installerPath -Destination $stagedInstaller -Force
$stagedProductFiles.Add($stagedInstaller)
$stagedPortable = Join-Path $productStagingDirectory $portableDeliveryFileName
Copy-Item -LiteralPath $publishedPortableExecutable -Destination $stagedPortable -Force
$stagedProductFiles.Add($stagedPortable)

foreach ($guideFile in $resolvedGuideFiles) {
    $destination = Join-Path $productStagingDirectory $guideFile.DestinationName
    Copy-Item -LiteralPath $guideFile.Source -Destination $destination -Force
    $stagedProductFiles.Add($destination)
}

$checksumPath = Join-Path $productStagingDirectory "SHA256SUMS.txt"
$checksumLines = foreach ($stagedFile in $stagedProductFiles) {
    $hash = (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash
    "$hash *$([System.IO.Path]::GetFileName($stagedFile))"
}
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding utf8
$stagedProductFiles.Add($checksumPath)

$ownedProductPatterns = @(
    "ordered-clicker-setup-v*.exe",
    "ordered-clicker-portable-v*.zip",
    "OrderedClicker.exe",
    "有序连点器-免安装.exe",
    "有序连点器-使用说明.pdf",
    "有序连点器-使用说明.html",
    "有序连点器-视频演示.mp4",
    "SHA256SUMS.txt"
)

$expectedProductNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($stagedFile in $stagedProductFiles) {
    $expectedProductNames.Add(
        [System.IO.Path]::GetFileName($stagedFile)) | Out-Null
}

if (Test-Path -LiteralPath $resolvedProductDirectory) {
    $unexpectedExistingEntries = @(
        Get-ChildItem -Force -LiteralPath $resolvedProductDirectory |
            Where-Object {
                if ($_.PSIsContainer) {
                    return $true
                }

                foreach ($pattern in $ownedProductPatterns) {
                    if ($_.Name -like $pattern) {
                        return $false
                    }
                }

                return $true
            }
    )
    if ($unexpectedExistingEntries.Count -gt 0) {
        $unexpectedNames = $unexpectedExistingEntries.Name -join "、"
        throw "产品目录包含非交付项：$unexpectedNames。请移走后重新构建。"
    }
}

$productParentDirectory = Split-Path -Parent $resolvedProductDirectory
New-Item -ItemType Directory -Force -Path $productParentDirectory | Out-Null
$deliveryId = [Guid]::NewGuid().ToString("N")
$safeDeliveryStagingDirectory = Assert-SafeRecursivePath `
    -Path (Join-Path $productParentDirectory ".ordered-clicker-staging-$deliveryId") `
    -ParentPath $productParentDirectory
$safeDeliveryBackupDirectory = Assert-SafeRecursivePath `
    -Path (Join-Path $productParentDirectory ".ordered-clicker-backup-$deliveryId") `
    -ParentPath $productParentDirectory
New-Item -ItemType Directory -Path $safeDeliveryStagingDirectory | Out-Null
$deliveryCommitted = $false
$preserveDeliveryBackup = $false

try {
    foreach ($stagedFile in $stagedProductFiles) {
        $destination = Join-Path `
            $safeDeliveryStagingDirectory `
            ([System.IO.Path]::GetFileName($stagedFile))
        Copy-Item -LiteralPath $stagedFile -Destination $destination
        $sourceHash = (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash
        $deliveryHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $deliveryHash) {
            throw "产品文件暂存校验失败：$destination"
        }
    }

    $stagingEntries = @(Get-ChildItem -Force -LiteralPath $safeDeliveryStagingDirectory)
    $unexpectedStagingEntries = @(
        $stagingEntries |
            Where-Object {
                $_.PSIsContainer -or -not $expectedProductNames.Contains($_.Name)
            }
    )
    if (
        $unexpectedStagingEntries.Count -gt 0 -or
        $stagingEntries.Count -ne $expectedProductNames.Count
    ) {
        throw "产品暂存目录未通过五文件完整性校验。"
    }

    $hadExistingProduct = Test-Path -LiteralPath $resolvedProductDirectory
    if ($hadExistingProduct) {
        Move-Item `
            -LiteralPath $resolvedProductDirectory `
            -Destination $safeDeliveryBackupDirectory
    }

    try {
        Move-Item `
            -LiteralPath $safeDeliveryStagingDirectory `
            -Destination $resolvedProductDirectory

        $productEntries = @(Get-ChildItem -Force -LiteralPath $resolvedProductDirectory)
        $unexpectedProductEntries = @(
            $productEntries |
                Where-Object {
                    $_.PSIsContainer -or -not $expectedProductNames.Contains($_.Name)
                }
        )
        if (
            $unexpectedProductEntries.Count -gt 0 -or
            $productEntries.Count -ne $expectedProductNames.Count
        ) {
            throw "最终产品目录未通过五文件完整性校验。"
        }

        $deliveryCommitted = $true
    }
    catch {
        $preserveDeliveryBackup = Test-Path -LiteralPath $safeDeliveryBackupDirectory
        if (Test-Path -LiteralPath $resolvedProductDirectory) {
            Remove-Item -LiteralPath $resolvedProductDirectory -Recurse -Force
        }
        if (Test-Path -LiteralPath $safeDeliveryBackupDirectory) {
            try {
                Move-Item `
                    -LiteralPath $safeDeliveryBackupDirectory `
                    -Destination $resolvedProductDirectory
                $preserveDeliveryBackup = $false
            }
            catch {
                $preserveDeliveryBackup = $true
                throw
            }
        }
        throw
    }
}
finally {
    foreach ($temporaryDirectory in @(
        $safeDeliveryStagingDirectory
    )) {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Assert-SafeRecursivePath `
                -Path $temporaryDirectory `
                -ParentPath $productParentDirectory | Out-Null
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }

    if (
        $deliveryCommitted -and
        -not $preserveDeliveryBackup -and
        (Test-Path -LiteralPath $safeDeliveryBackupDirectory)
    ) {
        Assert-SafeRecursivePath `
            -Path $safeDeliveryBackupDirectory `
            -ParentPath $productParentDirectory | Out-Null
        try {
            Remove-Item -LiteralPath $safeDeliveryBackupDirectory -Recurse -Force
        }
        catch {
            $preserveDeliveryBackup = $true
            Write-Warning "无法清理旧交付备份，已保留：$safeDeliveryBackupDirectory"
        }
    }
}

Write-Host ""
Write-Host "安装版构建完成：" -ForegroundColor Green
Write-Host "  安装包：$(Join-Path $resolvedProductDirectory $installerFileName)"
Write-Host "  免安装版：$(Join-Path $resolvedProductDirectory $portableDeliveryFileName)"
Write-Host "  PDF：$(Join-Path $resolvedProductDirectory '有序连点器-使用说明.pdf')"
Write-Host "  HTML：$(Join-Path $resolvedProductDirectory '有序连点器-使用说明.html')"
Write-Host "  校验文件：$(Join-Path $resolvedProductDirectory 'SHA256SUMS.txt')"
}
finally {
    if (
        $null -ne $signingCertificateState -and
        $signingCertificateState.RemoveAfterBuild
    ) {
        Remove-Item -LiteralPath (
            "Cert:\CurrentUser\My\$($signingCertificateState.Thumbprint)")
    }
}
