param(
    [int]$Port = 18082,
    [string]$UnityEditor = 'D:\Program Files\Unity 6000.0.30f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$goExe = Join-Path $repoRoot '.tools\go1.27.3\go\bin\go.exe'
$backendDir = Join-Path $repoRoot 'backend'
$runRoot = Join-Path ([IO.Path]::GetTempPath()) ('unity-inspection-playmode-' + [Guid]::NewGuid().ToString('N'))
$serverExe = Join-Path $runRoot 'inspection-server.exe'
$testResults = Join-Path $repoRoot 'Logs\unity-playmode-results.xml'
$unityLog = Join-Path $repoRoot 'Logs\unity-playmode.log'

if (-not (Test-Path -LiteralPath $UnityEditor)) {
    throw "Unity Editor was not found at $UnityEditor"
}
if (-not (Test-Path -LiteralPath $goExe)) {
    throw "Portable Go was not found at $goExe"
}

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'Logs') | Out-Null

Push-Location $backendDir
try {
    & $goExe build -o $serverExe ./cmd/server
    if ($LASTEXITCODE -ne 0) {
        throw "Go server build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$env:PORT = "$Port"
$env:DB_PATH = Join-Path $runRoot 'issues.db'
$env:CORS_ORIGINS = 'http://localhost:5173,http://127.0.0.1:5173'
$server = Start-Process -FilePath $serverExe -PassThru -WindowStyle Hidden

try {
    $healthy = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/healthz" -TimeoutSec 1
            if ($health.status -eq 'ok') {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $healthy) {
        throw "Test backend did not become healthy on port $Port."
    }

    $env:UNITY_INSPECTION_API_URL = "http://127.0.0.1:$Port"
    $arguments = @(
        '-batchmode',
        '-projectPath', $repoRoot,
        '-runTests',
        '-testPlatform', 'PlayMode',
        '-testResults', $testResults,
        '-logFile', $unityLog
    )

    $unity = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($unity.ExitCode -ne 0) {
        throw "Unity PlayMode tests failed with exit code $($unity.ExitCode). See $unityLog"
    }

    [xml]$results = Get-Content -Raw $testResults
    $testRun = $results.'test-run'
    Write-Host "Unity PlayMode tests: passed=$($testRun.passed), failed=$($testRun.failed), skipped=$($testRun.skipped)"
}
finally {
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id
        $server.WaitForExit()
    }
}
