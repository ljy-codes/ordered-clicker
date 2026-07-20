function Assert-SafeRecursivePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ParentPath
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\')
    $parentPrefix = $resolvedParent + '\'

    if (
        $resolvedPath.Equals($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedPath.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase)
    ) {
        throw "拒绝使用项目目录以外的递归路径：$resolvedPath"
    }

    $currentPath = $resolvedParent
    $relativePath = $resolvedPath.Substring($parentPrefix.Length)
    $segments = $relativePath.Split(
        [System.IO.Path]::DirectorySeparatorChar,
        [StringSplitOptions]::RemoveEmptyEntries)

    $pathsToCheck = [System.Collections.Generic.List[string]]::new()
    $pathsToCheck.Add($currentPath)
    foreach ($segment in $segments) {
        $currentPath = Join-Path $currentPath $segment
        $pathsToCheck.Add($currentPath)
    }

    foreach ($pathToCheck in $pathsToCheck) {
        if (-not (Test-Path -LiteralPath $pathToCheck)) {
            continue
        }

        $item = Get-Item -LiteralPath $pathToCheck -Force
        if (
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq
            [System.IO.FileAttributes]::ReparsePoint
        ) {
            throw "拒绝对包含重解析点的路径执行递归操作：$pathToCheck"
        }
    }

    return $resolvedPath
}
