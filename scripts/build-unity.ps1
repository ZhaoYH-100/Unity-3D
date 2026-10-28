param(
    [string]$UnityEditor = 'D:\Program Files\Unity 6000.0.30f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$logPath = Join-Path $repoRoot 'Logs\unity-build.log'

if (-not (Test-Path -LiteralPath $UnityEditor)) {
    throw "Unity Editor was not found at $UnityEditor"
}

$logDirectory = Split-Path -Parent $logPath
if (-not (Test-Path -LiteralPath $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

$arguments = @(
    '-batchmode',
    '-projectPath', $repoRoot,
    '-executeMethod', 'UnityInspection.EditorTools.InspectionBuild.BuildWindows',
    '-quit',
    '-logFile', $logPath
)

$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Unity Windows build failed with exit code $($process.ExitCode). See $logPath"
}

$binary = Join-Path $repoRoot 'Builds\Windows\UnityInspection.exe'
if (-not (Test-Path -LiteralPath $binary)) {
    throw "Unity reported success but the player binary was not found at $binary"
}

Write-Host "Unity Windows build: $binary"
