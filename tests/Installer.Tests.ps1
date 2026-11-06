$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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
Assert-Contains -Content $publishScript -Expected '[string]$Version = "2.2.0"' -Name "发布脚本接收版本"
Assert-Contains -Content $project -Expected "<Version>2.2.0</Version>" -Name "项目版本为 2.2.0"
Assert-Contains -Content $installer -Expected '#define AppVersion "2.2.0"' -Name "安装器默认版本为 2.2.0"
Assert-Contains -Content $buildScript -Expected '[string]$Version = "2.2.0"' -Name "构建脚本默认版本为 2.2.0"
foreach ($relativeProject in @(
    "src\OrderedClicker\OrderedClicker.csproj",
    "src\OrderedClicker.Shared\OrderedClicker.Shared.csproj",
    "src\OrderedClicker.Mac\OrderedClicker.Mac.csproj"
)) {
    [xml]$projectXml = Read-OptionalFile -Path (Join-Path $projectRoot $relativeProject)
    Assert-True -Condition (@($projectXml.Project.PropertyGroup.Version) -contains "2.2.0") `
        -Name "$relativeProject 版本一致"
}
foreach ($metadata in @("AssemblyVersion", "FileVersion", "InformationalVersion")) {
    $expectedVersion = if ($metadata -eq "InformationalVersion") { "2.2.0" } else { "2.2.0.0" }
    Assert-Contains -Content $project -Expected "<$metadata>$expectedVersion</$metadata>" `
        -Name "Windows $metadata 版本一致"
}
foreach ($relativeScript in @("scripts\build-macos.sh", "scripts\verify-macos-packages.sh")) {
    $macScript = Read-OptionalFile -Path (Join-Path $projectRoot $relativeScript)
    Assert-Contains -Content $macScript -Expected '${ORDERED_CLICKER_VERSION:-2.2.0}' `
        -Name "$relativeScript 默认版本一致"
}
$macDeliveryScript = Read-OptionalFile -Path (Join-Path $projectRoot "scripts\package-macos-delivery.py")
Assert-Contains -Content $macDeliveryScript -Expected "default='2.2.0'" -Name "Mac 交付脚本默认版本一致"
[xml]$macPlist = Read-OptionalFile -Path (Join-Path $projectRoot "installer\macos\Info.plist")
$plistKeys = @($macPlist.SelectNodes("/plist/dict/*"))
$shortVersionKey = @($plistKeys | Where-Object { $_.Name -eq "key" -and $_.InnerText -eq "CFBundleShortVersionString" })[0]
$buildVersionKey = @($plistKeys | Where-Object { $_.Name -eq "key" -and $_.InnerText -eq "CFBundleVersion" })[0]
Assert-True -Condition ($shortVersionKey.NextSibling.InnerText -eq "2.2.0") -Name "Mac 安装器显示版本一致"
Assert-True -Condition ($buildVersionKey.NextSibling.InnerText -eq "2200") -Name "Mac 安装器构建号递增"
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
Assert-Contains -Content $releaseWorkflow -Expected "JRSoftware.InnoSetup.7" -Name "发布安装 Inno Setup 7"
Assert-Contains -Content $releaseWorkflow -Expected "softprops/action-gh-release@v2" -Name "标签创建 GitHub Release"
Assert-Contains -Content $releaseWorkflow -Expected 'PYTHONUTF8: "1"' -Name "文档构建强制 UTF-8"
Assert-Contains `
    -Content $releaseWorkflow `
    -Expected 'ordered-clicker-portable-v$version.exe' `
    -Name "GitHub Release 使用稳定的英文免安装文件名"
Assert-Contains `
    -Content $releaseWorkflow `
    -Expected 'ordered-clicker-user-guide-v$version.pdf' `
    -Name "GitHub Release 使用稳定的英文 PDF 文件名"
Assert-Contains `
    -Content $releaseWorkflow `
    -Expected 'ordered-clicker-user-guide-v$version.html' `
    -Name "GitHub Release 使用稳定的英文 HTML 文件名"
Assert-Contains `
    -Content $releaseWorkflow `
    -Expected 'ordered-clicker-release' `
    -Name "GitHub Release 使用独立附件暂存目录"
Assert-NotContains `
    -Content $releaseWorkflow `
    -Unexpected '${{ runner.temp }}/ordered-clicker-product/有序连点器' `
    -Name "上传动作不直接处理中文产品路径"
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
foreach ($guideSource in @(
    "README.md",
    "scripts\guide\build_product_guide.py",
    "scripts\guide\build_pdf_guide.py",
    "scripts\build-macos-guide.py",
    "docs\macos\BUILD.md"
)) {
    $guideContent = Read-OptionalFile -Path (Join-Path $projectRoot $guideSource)
    foreach ($term in @("2.2.0", "演练", "单步", "窗口绑定", "自定义检测区域", "v4", "不改变正式执行断点")) {
        Assert-Contains -Content $guideContent -Expected $term -Name "$guideSource 包含 $term"
    }
}

$guidePreflightIndex = $buildScript.IndexOf("缺少指导文件", [StringComparison]::Ordinal)
$publishIndex = $buildScript.IndexOf("发布 Windows x64 自包含应用", [StringComparison]::Ordinal)
Assert-True `
    -Condition ($guidePreflightIndex -ge 0 -and $publishIndex -ge 0 -and $guidePreflightIndex -lt $publishIndex) `
    -Name "指导文件在发布前预检"

# Execute the workflow's real upload block; only the HTTP boundary is replaced.
function Test-ReleaseAssetReplacement {
    param([string]$Scenario)

    $stepMatch = [regex]::Match(
        $releaseWorkflow,
        '(?ms)^      - name: (?:Replace release assets|Clear existing release assets)\r?\n(?:(?!^      - ).)*?^        run: \|\r?\n(?<body>(?:^          [^\r\n]*(?:\r?\n|$)|^\r?\n)+)')
    if (-not $stepMatch.Success) {
        Assert-True -Condition $false -Name "可以提取实际发布步骤"
        return
    }
    $workflowCode = [regex]::Replace($stepMatch.Groups["body"].Value, '(?m)^          ', '')
    $workflowAction = [scriptblock]::Create($workflowCode)
    $fixtureRoot = Join-Path $projectRoot ".tmp\release-assets-$([Guid]::NewGuid().ToString('N'))"
    $stageDirectory = Join-Path $fixtureRoot "ordered-clicker-release"
    $savedEnvironment = @{}
    foreach ($key in @("RUNNER_TEMP", "RELEASE_DIRECTORY", "GITHUB_REPOSITORY", "GITHUB_REF_NAME", "GH_TOKEN")) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
    }
    $state = @{
        Assets = [System.Collections.Generic.List[object]]::new()
        Calls = [System.Collections.Generic.List[string]]::new()
        Uploads = 0
        NextId = 1000
    }
    $setupName = "ordered-clicker-setup-v2.2.0.exe"
    $unrelatedNames = @(
        "ordered-clicker-macos-arm64-v2.2.0.dmg",
        "ordered-clicker-macos-x64-v2.2.0.dmg",
        "SHA256SUMS-macos.txt",
        "manual-notes.txt",
        "ordered-clicker-setup-v2.2.0.exe.backup"
    )
    foreach ($name in @($setupName, "SHA256SUMS.txt") + $unrelatedNames) {
        $state.Assets.Add([pscustomobject]@{ id = $state.Assets.Count + 1; name = $name; content = "original:$name" })
    }
    # Put another exact match beyond the first API page.
    for ($index = 0; $index -lt 100; $index++) {
        $state.Assets.Add([pscustomobject]@{ id = $state.Assets.Count + 1; name = "other-$index.txt"; content = "keep" })
    }
    $state.Assets.Add([pscustomobject]@{ id = 200; name = "release-guide.html"; content = "old-guide" })

    function Invoke-RestMethod {
        param($Method, $Uri, $Headers, $InFile, $ContentType, $Body, $ErrorAction,
            [switch]$SkipHttpErrorCheck, [string]$StatusCodeVariable)
        $state.Calls.Add("$Method $Uri")
        $request = [uri]$Uri
        if ($Method -eq "Get" -and $request.AbsolutePath -match '/releases/tags/') {
            if ($StatusCodeVariable) { Set-Variable -Name $StatusCodeVariable -Value 200 -Scope 1 }
            return [pscustomobject]@{
                id = 99
                upload_url = 'https://uploads.github.test/repos/test/repo/releases/99/assets{?name,label}'
                assets = @($state.Assets | Select-Object -First 100)
            }
        }
        if ($Method -eq "Get" -and $request.AbsolutePath -match '/releases/99/assets$') {
            if ($Scenario -eq "QueryFailure") { throw "simulated asset query failure" }
            $pageMatch = [regex]::Match($request.Query, '(?:[?&])page=(\d+)')
            $page = if ($pageMatch.Success) { [int]$pageMatch.Groups[1].Value } else { 1 }
            return @($state.Assets | Select-Object -Skip (($page - 1) * 100) -First 100)
        }
        if ($Method -eq "Post") {
            $state.Uploads++
            if ($Scenario -eq "UploadFailure" -and $state.Uploads -eq 2) {
                throw "simulated upload failure"
            }
            $nameMatch = [regex]::Match($request.Query, '(?:[?&])name=([^&]+)')
            $name = [uri]::UnescapeDataString($nameMatch.Groups[1].Value)
            if (@($state.Assets | Where-Object { $_.name -ceq $name }).Count -gt 0) {
                throw "duplicate asset name: $name"
            }
            $asset = [pscustomobject]@{
                id = $state.NextId++
                name = $name
                content = [System.IO.File]::ReadAllText($InFile)
            }
            $state.Assets.Add($asset)
            return $asset
        }
        if ($Method -eq "Delete" -or $Method -eq "Patch") {
            $id = [int]($request.Segments[-1])
            $asset = @($state.Assets | Where-Object { $_.id -eq $id })[0]
            if (-not $asset) { throw "unknown asset: $id" }
            if ($Method -eq "Delete") {
                [void]$state.Assets.Remove($asset)
                return
            }
            $newName = ($Body | ConvertFrom-Json).name
            if (@($state.Assets | Where-Object { $_.name -ceq $newName }).Count -gt 0) {
                throw "duplicate rename: $newName"
            }
            $asset.name = $newName
            return $asset
        }
        throw "Unexpected release API request: $Method $Uri"
    }

    try {
        New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
        if ($Scenario -ne "EmptyStage") {
            foreach ($name in @($setupName, "SHA256SUMS.txt", "release-guide.html")) {
                [System.IO.File]::WriteAllText((Join-Path $stageDirectory $name), "new:$name")
            }
        }
        $env:RUNNER_TEMP = $fixtureRoot
        $env:RELEASE_DIRECTORY = $stageDirectory
        $env:GITHUB_REPOSITORY = "test/repo"
        $env:GITHUB_REF_NAME = "v2.2.0"
        $env:GH_TOKEN = "offline-fixture"
        $failure = $null
        try { & $workflowAction }
        catch { $failure = $_.Exception.Message }

        if ($Scenario -eq "Success") {
            Assert-True -Condition ($null -eq $failure) -Name "实际工作流附件替换成功：$failure"
            foreach ($name in @($setupName, "SHA256SUMS.txt", "release-guide.html")) {
                $matches = @($state.Assets | Where-Object { $_.name -ceq $name })
                Assert-True -Condition ($matches.Count -eq 1 -and $matches[0].content -ceq "new:$name") `
                    -Name "同名附件替换且内容正确：$name"
            }
            Assert-True -Condition ($state.Assets.Count -eq 108) -Name "分页替换后无临时附件或额外删除"
            $deletions = @($state.Calls -match '^Delete ')
            $lastUploadIndex = -1
            $firstDeleteIndex = -1
            for ($index = 0; $index -lt $state.Calls.Count; $index++) {
                if ($state.Calls[$index] -like "Post *") { $lastUploadIndex = $index }
                if ($state.Calls[$index] -like "Delete *" -and $firstDeleteIndex -lt 0) { $firstDeleteIndex = $index }
            }
            Assert-True -Condition ($deletions.Count -eq 3 -and $firstDeleteIndex -gt $lastUploadIndex) `
                -Name "全部上传成功后才删除精确同名旧附件"
        }
        elseif ($Scenario -eq "UploadFailure") {
            Assert-True -Condition ($failure -like "*simulated upload failure*") -Name "上传失败向 CI 报错"
            foreach ($name in @($setupName, "SHA256SUMS.txt")) {
                $matches = @($state.Assets | Where-Object { $_.name -ceq $name })
                Assert-True -Condition ($matches.Count -eq 1 -and $matches[0].content -ceq "original:$name") `
                    -Name "上传失败保留旧附件：$name"
            }
            Assert-True -Condition (@($state.Assets | Where-Object { $_.id -ge 1000 }).Count -eq 0) `
                -Name "上传失败清理本次临时附件"
            Assert-True -Condition ($state.Assets.Count -eq 108) -Name "上传失败原有附件总数不变"
        }
        elseif ($Scenario -eq "QueryFailure") {
            Assert-True -Condition ($failure -like "*simulated asset query failure*") -Name "附件查询失败向 CI 报错"
            Assert-True -Condition ($state.Uploads -eq 0 -and $state.Assets.Count -eq 108) `
                -Name "查询失败不上传或删除附件"
        }
        else {
            Assert-True -Condition ($null -ne $failure) -Name "附件目录为空时失败"
            Assert-True -Condition ($state.Calls.Count -eq 0) -Name "附件目录为空时不调用发布 API"
        }
        foreach ($name in $unrelatedNames) {
            $matches = @($state.Assets | Where-Object { $_.name -ceq $name })
            Assert-True -Condition ($matches.Count -eq 1 -and $matches[0].content -ceq "original:$name") `
                -Name "$Scenario 保留其他平台及非同名附件：$name"
        }
        if ($Scenario -ne "Success") {
            Assert-True -Condition (@($state.Assets | Where-Object { $_.id -eq 200 -and $_.content -eq "old-guide" }).Count -eq 1) `
                -Name "$Scenario 保留分页后旧附件"
        }
    }
    finally {
        foreach ($key in $savedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], "Process")
        }
        $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
        $expectedParent = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ".tmp")) + '\'
        if (-not $resolvedFixture.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe release test cleanup path: $resolvedFixture"
        }
        if (Test-Path -LiteralPath $resolvedFixture) {
            Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
        }
    }
}

foreach ($scenario in @("Success", "UploadFailure", "EmptyStage", "QueryFailure")) {
    Test-ReleaseAssetReplacement -Scenario $scenario
}

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
