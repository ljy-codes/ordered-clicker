$projectRoot = Split-Path -Parent $PSScriptRoot

$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $projectRoot ".nuget\packages"
$env:APPDATA = Join-Path $projectRoot ".dotnet-home\AppData\Roaming"
$env:LOCALAPPDATA = Join-Path $projectRoot ".dotnet-home\AppData\Local"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$localDotnet = Join-Path $projectRoot ".tools\dotnet\dotnet.exe"
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw "未找到 .NET SDK。请安装 .NET 10 SDK，或将 SDK 放到 .tools\dotnet。"
    }

    $dotnet = $dotnetCommand.Source
}

& $dotnet @args
exit $LASTEXITCODE
