$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $projectRoot "installer\OrderedClicker.iss"
$projectPath = Join-Path $projectRoot "src\OrderedClicker\OrderedClicker.csproj"
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

$installer = Read-OptionalFile -Path $installerPath
$project = Read-OptionalFile -Path $projectPath

Assert-Contains -Content $installer -Expected "PrivilegesRequired=lowest" -Name "当前用户安装"
Assert-Contains -Content $installer -Expected 'DefaultDirName={localappdata}\Programs\OrderedClicker' -Name "用户安装目录"
Assert-Contains -Content $installer -Expected "AppId={{" -Name "稳定 AppId"
Assert-Contains -Content $installer -Expected 'Name: "desktopicon"' -Name "桌面快捷方式任务"
Assert-Contains -Content $installer -Expected 'Filename: "{app}\OrderedClicker.exe"' -Name "应用快捷方式"
Assert-Contains -Content $installer -Expected "Flags: nowait postinstall skipifsilent" -Name "安装后启动"
Assert-Contains `
    -Content $project `
    -Expected "<ApplicationIcon>..\..\installer\assets\ordered-clicker.ico</ApplicationIcon>" `
    -Name "应用图标配置"

if ($script:failures -gt 0) {
    Write-Host "Installer contract tests failed: $script:failures" -ForegroundColor Red
    exit 1
}

Write-Host "Installer contract tests passed." -ForegroundColor Green
