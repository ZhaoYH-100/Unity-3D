$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$goBin = Join-Path $repoRoot '.tools\go1.27.3\go\bin'
$nodeBin = Join-Path $repoRoot '.tools\node-v24.22.1\node-v24.22.1-win-x64'

if (-not (Test-Path -LiteralPath (Join-Path $goBin 'go.exe'))) {
    throw "Portable Go was not found at $goBin"
}

if (-not (Test-Path -LiteralPath (Join-Path $nodeBin 'node.exe'))) {
    throw "Portable Node was not found at $nodeBin"
}

$env:PATH = "$goBin;$nodeBin;$env:PATH"
Write-Host "go      $(& (Join-Path $goBin 'go.exe') version)"
Write-Host "node    $(& (Join-Path $nodeBin 'node.exe') --version)"
Write-Host "npm     $(& (Join-Path $nodeBin 'npm.cmd') --version)"
