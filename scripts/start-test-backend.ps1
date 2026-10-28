param(
    [int]$Port = 18080
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$goExe = Join-Path $repoRoot '.tools\go1.27.3\go\bin\go.exe'
$backendDir = Join-Path $repoRoot 'backend'
$testDataDir = Join-Path ([IO.Path]::GetTempPath()) ("unity-inspection-e2e-" + [Guid]::NewGuid().ToString('N'))

if (-not (Test-Path -LiteralPath $goExe)) {
    throw "Portable Go was not found at $goExe"
}

New-Item -ItemType Directory -Force -Path $testDataDir | Out-Null
$env:PORT = "$Port"
$env:DB_PATH = Join-Path $testDataDir 'issues.db'
$env:CORS_ORIGINS = 'http://127.0.0.1:15173,http://localhost:15173'

Push-Location $backendDir
try {
    & $goExe run ./cmd/server
}
finally {
    Pop-Location
}
