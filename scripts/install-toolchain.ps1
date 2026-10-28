$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$downloadDir = Join-Path $repoRoot '.tools\downloads'
$goRoot = Join-Path $repoRoot '.tools\go1.27.3'
$goExe = Join-Path $goRoot 'go\bin\go.exe'
$nodeRoot = Join-Path $repoRoot '.tools\node-v24.22.1'
$nodeExe = Join-Path $nodeRoot 'node-v24.22.1-win-x64\node.exe'

$goArchive = Join-Path $downloadDir 'go1.27.3.windows-amd64.zip'
$goUrl = 'https://go.dev/dl/go1.27.3.windows-amd64.zip'
$goSha256 = 'f26081f438319757f8a1b46d7d0b2c5ec7583d4038e485e0aa725cfde7885fff'

$nodeArchive = Join-Path $downloadDir 'node-v24.22.1-win-x64.zip'
$nodeUrl = 'https://nodejs.org/dist/v24.22.1/node-v24.22.1-win-x64.zip'
$nodeSha256 = 'd2046d28c2c3dcd211ac5145c0bf30e2047ef9dfb306693c8c6e2eccd8b95fa5'

New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

function Get-VerifiedArchive {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Destination)) {
        & curl.exe -fL --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 900 -o $Destination $Url
        if ($LASTEXITCODE -ne 0) {
            throw "Download failed: $Url"
        }
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "Checksum mismatch for $Destination. Expected $ExpectedSha256, got $actual"
    }
}

if (-not (Test-Path -LiteralPath $goExe)) {
    Get-VerifiedArchive -Url $goUrl -Destination $goArchive -ExpectedSha256 $goSha256
    New-Item -ItemType Directory -Force -Path $goRoot | Out-Null
    & tar.exe -xf $goArchive -C $goRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Go archive extraction failed"
    }
}

if (-not (Test-Path -LiteralPath $nodeExe)) {
    Get-VerifiedArchive -Url $nodeUrl -Destination $nodeArchive -ExpectedSha256 $nodeSha256
    New-Item -ItemType Directory -Force -Path $nodeRoot | Out-Null
    & tar.exe -xf $nodeArchive -C $nodeRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Node archive extraction failed"
    }
}

& $goExe version
& $nodeExe --version
