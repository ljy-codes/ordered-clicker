$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $projectRoot "installer\OrderedClicker.iss"
$projectPath = Join-Path $projectRoot "src\OrderedClicker\OrderedClicker.csproj"
$buildScriptPath = Join-Path $projectRoot "scripts\build-installer.ps1"
$publishScriptPath = Join-Path $projectRoot "scripts\publish.ps1"
$pathSafetyPath = Join-Path $projectRoot "scripts\path-safety.ps1"
$readmePath = Join-Path $projectRoot "README.md"
$releaseWorkflowPath = Join-Path $projectRoot ".github\workflows\ci-release.yml"
$pdfGuideScriptPath = Join-Path $projectRoot "scripts\guide\build_pdf_guide.py"
$script:failures = 0

function Read-OptionalFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "[FAIL] 文件不存在：$Path" -ForegroundColor Red
        $script:failures++
        return ""
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Expected,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($Content.Contains($Expected, [StringComparison]::Ordinal)) {
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return
    }

    Write-Host "[FAIL] $Name，缺少：$Expected" -ForegroundColor Red
    $script:failures++
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Unexpected,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not $Content.Contains($Unexpected, [StringComparison]::Ordinal)) {
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return
    }

    Write-Host "[FAIL] $Name，不应包含：$Unexpected" -ForegroundColor Red
    $script:failures++
}

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($Condition) {
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return
    }

    Write-Host "[FAIL] $Name" -ForegroundColor Red
    $script:failures++
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [Parameter(Mandatory)]
        [string]$ExpectedMessage,

        [Parameter(Mandatory)]
        [string]$Name
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message.Contains($ExpectedMessage, [StringComparison]::Ordinal)) {
            Write-Host "[PASS] $Name" -ForegroundColor Green
            return
        }

        Write-Host "[FAIL] $Name，异常不匹配：$($_.Exception.Message)" -ForegroundColor Red
        $script:failures++
        return
    }

    Write-Host "[FAIL] $Name，未抛出异常" -ForegroundColor Red
    $script:failures++
}

$installer = Read-OptionalFile -Path $installerPath
$project = Read-OptionalFile -Path $projectPath
$buildScript = Read-OptionalFile -Path $buildScriptPath
$publishScript = Read-OptionalFile -Path $publishScriptPath
$pathSafety = Read-OptionalFile -Path $pathSafetyPath
$readme = Read-OptionalFile -Path $readmePath
$releaseWorkflow = Read-OptionalFile -Path $releaseWorkflowPath
$pdfGuideScript = Read-OptionalFile -Path $pdfGuideScriptPath

Assert-Contains -Content $installer -Expected "PrivilegesRequired=lowest" -Name "当前用户安装"
Assert-Contains -Content $installer -Expected 'DefaultDirName={localappdata}\Programs\OrderedClicker' -Name "用户安装目录"
Assert-Contains `
    -Content $installer `
    -Expected "AppId={{D25D23BA-1607-4D23-82BD-D1891DD195A5}" `
    -Name "固定 AppId"
Assert-Contains -Content $installer -Expected 'Name: "desktopicon"' -Name "桌面快捷方式任务"
Assert-Contains -Content $installer -Expected 'Filename: "{app}\OrderedClicker.exe"' -Name "应用快捷方式"
Assert-Contains -Content $installer -Expected "Flags: nowait postinstall skipifsilent" -Name "安装后启动"
Assert-Contains `
    -Content $project `
    -Expected "<ApplicationIcon>..\..\installer\assets\ordered-clicker.ico</ApplicationIcon>" `
    -Name "应用图标配置"
Assert-Contains `
    -Content $project `
    -Expected "<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>" `
    -Name "产品版本不附加提交哈希"
Assert-Contains -Content $buildScript -Expected "publish.ps1" -Name "复用发布脚本"
Assert-Contains -Content $buildScript -Expected '-Portable' -Name "免安装版使用独立编译模式"
Assert-Contains -Content $publishScript -Expected 'DefineConstants=PORTABLE' -Name "便携模式使用编译常量"
Assert-Contains -Content $buildScript -Expected "Compress-Archive" -Name "生成便携版"
Assert-Contains `
    -Content $buildScript `
    -Expected '"有序连点器-免安装.exe"' `
    -Name "产品目录使用明确的中文免安装文件名"
Assert-Contains -Content $buildScript -Expected "Get-FileHash" -Name "校验交付复制"
Assert-Contains -Content $buildScript -Expected '"SHA256SUMS.txt"' -Name "生成 SHA-256 清单"
Assert-Contains -Content $buildScript -Expected "有序连点器-使用说明.pdf" -Name "复制 PDF"
Assert-Contains -Content $buildScript -Expected "有序连点器-使用说明.html" -Name "复制 HTML"
Assert-NotContains `
    -Content $buildScript `
    -Unexpected '"有序连点器-完整操作教程.mp4" =' `
    -Name "视频不再作为交付依赖"
Assert-Contains -Content $buildScript -Expected '$PSVersionTable.PSVersion.Major -lt 7' -Name "要求 PowerShell 7"
Assert-Contains -Content $buildScript -Expected '& (Join-Path $PSScriptRoot "publish.ps1") -Version $Version' -Name "发布版本透传"
Assert-Contains -Content $buildScript -Expected '"product-staging"' -Name "产品文件先暂存"
Assert-Contains `
    -Content $buildScript `
    -Expected 'Join-Path $projectRoot "有序连点器"' `
    -Name "默认产品目录位于连点器项目内"
Assert-Contains -Content $buildScript -Expected '$ownedProductPatterns' -Name "清理旧版本产品文件"
Assert-Contains -Content $buildScript -Expected '".ordered-clicker-backup-$deliveryId"' -Name "交付目录支持事务回滚"
Assert-Contains `
    -Content $buildScript `
    -Expected "产品目录包含非交付项" `
    -Name "拒绝产品目录残留额外文件或目录"
Assert-Contains -Content $buildScript -Expected '"OrderedClicker.exe"' -Name "清理旧免安装 EXE"
Assert-Contains -Content $buildScript -Expected '$expectedProductNames' -Name "产品目录包含校验文件"
Assert-Contains -Content $buildScript -Expected "Assert-SafeRecursivePath" -Name "打包清理路径安全检查"
Assert-NotContains -Content $buildScript -Unexpected "Inno Setup 6" -Name "只允许 Inno Setup 7"
Assert-Contains `
    -Content $publishScript `
    -Expected '$unexpectedRuntimeFiles = @(' `
    -Name "严格模式下稳定检查发布文件"
Assert-Contains -Content $publishScript -Expected '[string]$Version = "2.1.0"' -Name "发布脚本接收版本"
Assert-Contains -Content $project -Expected "<Version>2.1.0</Version>" -Name "项目版本为 2.1.0"
Assert-Contains -Content $installer -Expected '#define AppVersion "2.1.0"' -Name "安装器默认版本为 2.1.0"
Assert-Contains -Content $buildScript -Expected '[string]$Version = "2.1.0"' -Name "构建脚本默认版本为 2.1.0"
Assert-Contains -Content $buildScript -Expected '[switch]$Release' -Name "发布构建显式区分签名模式"
Assert-Contains -Content $buildScript -Expected '[string]$SigningCertificatePath' -Name "发布构建接收签名证书"
Assert-Contains -Content $buildScript -Expected 'ORDERED_CLICKER_SIGNING_PASSWORD' -Name "签名密码从环境变量读取"
Assert-Contains `
    -Content $buildScript `
    -Expected 'Remove-Item Env:\ORDERED_CLICKER_SIGNING_PASSWORD' `
    -Name "签名密码读取后立即从环境变量清除"
Assert-Contains -Content $buildScript -Expected 'Import-PfxCertificate' -Name "签名证书导入用户证书库"
Assert-Contains -Content $buildScript -Expected '/sha1 $CertificateThumbprint' -Name "签名按证书指纹执行"
Assert-NotContains -Content $buildScript -Unexpected '/p $Password' -Name "签名密码不进入原生命令行"
Assert-Contains -Content $buildScript -Expected '[string]$TimestampUrl' -Name "发布构建接收时间戳地址"
Assert-Contains -Content $buildScript -Expected 'Get-AuthenticodeSignature' -Name "发布构建验证 Authenticode 签名"
Assert-Contains -Content $publishScript -Expected '"-p:FileVersion=${Version}.0"' -Name "EXE 文件版本透传"
Assert-Contains -Content $publishScript -Expected "Assert-SafeRecursivePath" -Name "发布清理路径安全检查"
Assert-Contains -Content $pathSafety -Expected "[System.IO.FileAttributes]::ReparsePoint" -Name "拒绝重解析点"
Assert-Contains -Content $readme -Expected "PowerShell 7" -Name "构建依赖说明"
Assert-Contains -Content $readme -Expected "最终产品目录只包含" -Name "最小交付说明"
Assert-Contains -Content $releaseWorkflow -Expected "scripts/test.ps1" -Name "CI 使用统一测试入口"
Assert-Contains -Content $releaseWorkflow -Expected "refs/tags/v" -Name "版本标签触发发布"
Assert-Contains -Content $releaseWorkflow -Expected "JRSoftware.InnoSetup" -Name "发布安装 Inno Setup 7"
Assert-Contains -Content $releaseWorkflow -Expected "softprops/action-gh-release@v2" -Name "标签创建 GitHub Release"
Assert-Contains -Content $releaseWorkflow -Expected 'PYTHONUTF8: "1"' -Name "文档构建强制 UTF-8"
Assert-Contains -Content $pdfGuideScript -Expected 'UnicodeCIDFont' -Name "PDF 支持无本机中文字体回退"
Assert-Contains -Content $pdfGuideScript -Expected '"STSong-Light"' -Name "PDF 使用内置中文 CID 字体"
Assert-Contains `
    -Content $buildScript `
    -Expected '$deliveryCommitted' `
    -Name "交付备份清理由提交状态控制"
Assert-Contains `
    -Content $buildScript `
    -Expected '$preserveDeliveryBackup' `
    -Name "回滚失败时保留唯一交付备份"
Assert-Contains `
    -Content $buildScript `
    -Expected '$preserveDeliveryBackup = Test-Path -LiteralPath $safeDeliveryBackupDirectory' `
    -Name "回滚清理前先锁定备份保留状态"
Assert-Contains `
    -Content $buildScript `
    -Expected '无法清理旧交付备份，已保留' `
    -Name "提交成功后的备份清理失败只告警并保留"
Assert-Contains `
    -Content $readme `
    -Expected "安装版 EXE、免安装 EXE、PDF 使用说明、HTML 使用说明和 SHA256SUMS.txt" `
    -Name "五文件交付说明"

$guidePreflightIndex = $buildScript.IndexOf("缺少指导文件", [StringComparison]::Ordinal)
$publishIndex = $buildScript.IndexOf("发布 Windows x64 自包含应用", [StringComparison]::Ordinal)
Assert-True `
    -Condition ($guidePreflightIndex -ge 0 -and $publishIndex -ge 0 -and $guidePreflightIndex -lt $publishIndex) `
    -Name "指导文件在发布前预检"

if (Test-Path -LiteralPath $pathSafetyPath -PathType Leaf) {
    . $pathSafetyPath
    $safetyTestRoot = Join-Path $projectRoot ".tmp\installer-path-safety-$([Guid]::NewGuid().ToString('N'))"
    $normalDirectory = Join-Path $safetyTestRoot "normal\child"
    $junctionTarget = Join-Path $safetyTestRoot "junction-target"
    $junctionPath = Join-Path $safetyTestRoot "junction"

    try {
        New-Item -ItemType Directory -Force -Path $normalDirectory, $junctionTarget | Out-Null
        $resolvedNormal = Assert-SafeRecursivePath -Path $normalDirectory -ParentPath $projectRoot
        Assert-True `
            -Condition ($resolvedNormal -eq [System.IO.Path]::GetFullPath($normalDirectory)) `
            -Name "允许普通项目内路径"
        Assert-Throws `
            -Action { Assert-SafeRecursivePath -Path (Join-Path $projectRoot "..\outside") -ParentPath $projectRoot } `
            -ExpectedMessage "以外" `
            -Name "拒绝项目外路径"

        New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
        Assert-Throws `
            -Action { Assert-SafeRecursivePath -Path (Join-Path $junctionPath "child") -ParentPath $projectRoot } `
            -ExpectedMessage "重解析点" `
            -Name "拒绝 Junction 路径"
    }
    finally {
        if (Test-Path -LiteralPath $junctionPath) {
            Remove-Item -LiteralPath $junctionPath -Force
        }

        $resolvedSafetyTestRoot = [System.IO.Path]::GetFullPath($safetyTestRoot)
        $resolvedProjectRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
        if (
            $resolvedSafetyTestRoot.StartsWith(
                $resolvedProjectRoot,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedSafetyTestRoot)
        ) {
            Remove-Item -LiteralPath $resolvedSafetyTestRoot -Recurse -Force
        }
    }
}

if ($script:failures -gt 0) {
    Write-Host "Installer contract tests failed: $script:failures" -ForegroundColor Red
    exit 1
}

Write-Host "Installer contract tests passed." -ForegroundColor Green
