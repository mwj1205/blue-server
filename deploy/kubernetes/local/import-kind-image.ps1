[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Image,
    [string]$ControlPlaneContainer = "desktop-control-plane"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Docker {
    param([string[]]$Arguments)

    $output = @(& docker @Arguments 2>&1)
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $detail = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "docker command failed (ExitCode=$exitCode).$([Environment]::NewLine)$detail"
    }

    return $output
}

if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker was not found. Install Docker Desktop and add docker to PATH."
}

Invoke-Docker -Arguments @("image", "inspect", $Image) | Out-Null
Invoke-Docker -Arguments @(
    "inspect",
    "--type", "container",
    $ControlPlaneContainer
) | Out-Null

$archiveName = "blue-server-image-$([Guid]::NewGuid().ToString('N')).tar"
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$archivePath = Join-Path $temporaryRoot $archiveName
$containerArchivePath = "/$archiveName"
$containerDestination = "${ControlPlaneContainer}:$containerArchivePath"

try {
    Invoke-Docker -Arguments @(
        "save",
        "--output", $archivePath,
        $Image
    ) | Out-Null

    Invoke-Docker -Arguments @(
        "cp",
        $archivePath,
        $containerDestination
    ) | Out-Null

    Invoke-Docker -Arguments @(
        "exec",
        $ControlPlaneContainer,
        "ctr",
        "--namespace", "k8s.io",
        "images", "import",
        $containerArchivePath
    ) | Out-Null
}
finally {
    & docker exec $ControlPlaneContainer `
        rm -f $containerArchivePath 2>$null

    if (Test-Path -LiteralPath $archivePath) {
        $resolvedArchivePath = [IO.Path]::GetFullPath($archivePath)

        if (-not $resolvedArchivePath.StartsWith(
                $temporaryRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected temporary archive path: $resolvedArchivePath"
        }

        Remove-Item -LiteralPath $resolvedArchivePath -Force
    }
}

Write-Host "Image imported into Kubernetes node. Image=$Image, Node=$ControlPlaneContainer"
