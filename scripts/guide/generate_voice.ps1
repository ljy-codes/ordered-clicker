param(
    [string]$Rate = "-8%"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$python = "C:\Users\nb-liuxinsheng\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
$dependencyPath = Join-Path $root ".tools\python"
$scriptPath = Join-Path $PSScriptRoot "generate_voice.py"

$env:PYTHONPATH = $dependencyPath
& $python $scriptPath "--rate=$Rate"
exit $LASTEXITCODE
