$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $projectRoot "installer\OrderedClicker.iss"
$projectPath = Join-Path $projectRoot "src\OrderedClicker\OrderedClicker.csproj"
$buildScriptPath = Join-Path $projectRoot "scripts\build-installer.ps1"
$publishScriptPath = Join-Path $projectRoot "scripts\publish.ps1"
$pathSafetyPath = Join-Path $projectRoot "scripts\path-safety.ps1"
$readmePath = Join-Path $projectRoot "README.md"
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
Assert-Contains -Content $buildScript -Expected "Compress-Archive" -Name "生成便携版"
Assert-Contains -Content $buildScript -Expected "Get-FileHash" -Name "校验交付复制"
Assert-Contains -Content $buildScript -Expected "有序连点器-使用说明.pdf" -Name "复制 PDF"
Assert-Contains -Content $buildScript -Expected "有序连点器-使用说明.html" -Name "复制 HTML"
Assert-NotContains `
    -Content $buildScript `
    -Unexpected '"有序连点器-完整操作教程.mp4" =' `
    -Name "视频不再作为交付依赖"
Assert-Contains -Content $buildScript -Expected '$PSVersionTable.PSVersion.Major -lt 7' -Name "要求 PowerShell 7"
Assert-Contains -Content $buildScript -Expected '& (Join-Path $PSScriptRoot "publish.ps1") -Version $Version' -Name "发布版本透传"
Assert-Contains -Content $buildScript -Expected '"product-staging"' -Name "产品文件先暂存"
Assert-Contains -Content $buildScript -Expected '$ownedProductPatterns' -Name "清理旧版本产品文件"
Assert-Contains `
    -Content $buildScript `
    -Expected 'foreach ($packagePath in @($installerPath))' `
    -Name "产品目录只复制安装包"
Assert-NotContains `
    -Content $buildScript `
    -Unexpected '$currentProductNames.Add("SHA256SUMS.txt")' `
    -Name "产品目录不生成校验文件"
Assert-Contains -Content $buildScript -Expected "Assert-SafeRecursivePath" -Name "打包清理路径安全检查"
Assert-NotContains -Content $buildScript -Unexpected "Inno Setup 6" -Name "只允许 Inno Setup 7"
Assert-Contains `
    -Content $publishScript `
    -Expected '$unexpectedRuntimeFiles = @(' `
    -Name "严格模式下稳定检查发布文件"
Assert-Contains -Content $publishScript -Expected '[string]$Version = "1.3.1"' -Name "发布脚本接收版本"
Assert-Contains -Content $project -Expected "<Version>1.3.1</Version>" -Name "项目版本为 1.3.1"
Assert-Contains -Content $installer -Expected '#define AppVersion "1.3.1"' -Name "安装器默认版本为 1.3.1"
Assert-Contains -Content $buildScript -Expected '[string]$Version = "1.3.1"' -Name "构建脚本默认版本为 1.3.1"
Assert-Contains -Content $publishScript -Expected '"-p:FileVersion=${Version}.0"' -Name "EXE 文件版本透传"
Assert-Contains -Content $publishScript -Expected "Assert-SafeRecursivePath" -Name "发布清理路径安全检查"
Assert-Contains -Content $pathSafety -Expected "[System.IO.FileAttributes]::ReparsePoint" -Name "拒绝重解析点"
Assert-Contains -Content $readme -Expected "PowerShell 7" -Name "构建依赖说明"
Assert-Contains -Content $readme -Expected "最终产品目录只包含" -Name "最小交付说明"

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
