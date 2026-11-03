<#
.SYNOPSIS
    Downloads Google's MediaPipe hand landmarker (Apache-2.0) and converts its two models to ONNX for XrealScreen.Vision.
.DESCRIPTION
    Output: artifacts\models\hands\palm.onnx, landmarks.onnx, LICENSE-mediapipe.txt, SOURCE.txt (M9 E2, ADR-0016).
    Needs Python 3.10-3.12 on PATH. Creates a private virtual environment under artifacts\models\.convert-venv with
    pinned tensorflow-cpu and tf2onnx (~600 MB, only for the conversion; nothing of it ships).
.EXAMPLE
    .\tools\Convert-HandModels.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\artifacts\models\hands'),
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$taskUrl = 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task'
$taskSha256 = 'FBC2A30080C3C557093B5DDFC334698132EB341044CCEE322CCF8BCF3607CDE1'
$licenseUrl = 'https://raw.githubusercontent.com/google-ai-edge/mediapipe/master/LICENSE'
$packages = @('tensorflow-cpu==2.21.0', 'tf2onnx==1.17.0', 'onnx==1.23.2')

$OutDir = [IO.Path]::GetFullPath($OutDir)
if (-not $Force -and (Test-Path (Join-Path $OutDir 'palm.onnx')) -and (Test-Path (Join-Path $OutDir 'landmarks.onnx'))) {
    Write-Host "Models already in $OutDir (use -Force to rebuild)."
    return
}

$work = Join-Path ([IO.Path]::GetDirectoryName($OutDir)) '.convert'
$venv = Join-Path ([IO.Path]::GetDirectoryName($OutDir)) '.convert-venv'
New-Item -ItemType Directory -Force $OutDir, $work | Out-Null

$task = Join-Path $work 'hand_landmarker.task'
if (-not (Test-Path $task) -or (Get-FileHash $task).Hash -ne $taskSha256) {
    Write-Host "Downloading $taskUrl"
    Invoke-WebRequest $taskUrl -OutFile $task
}
$hash = (Get-FileHash $task).Hash
if ($hash -ne $taskSha256) { throw "Unexpected model hash $hash (expected $taskSha256)." }

Expand-Archive -Path $task -DestinationPath $work -Force   # .task is a zip: hand_detector.tflite + hand_landmarks_detector.tflite

$python = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path $python)) {
    Write-Host "Creating conversion environment in $venv"
    python -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'python -m venv failed (Python 3.10-3.12 on PATH?).' }
}
& $python -m pip install --quiet --disable-pip-version-check @packages
if ($LASTEXITCODE -ne 0) { throw 'pip install failed.' }

foreach ($pair in @(@('hand_detector.tflite', 'palm.onnx'), @('hand_landmarks_detector.tflite', 'landmarks.onnx'))) {
    $src = Join-Path $work $pair[0]
    $dst = Join-Path $OutDir $pair[1]
    Write-Host "Converting $($pair[0]) -> $dst"
    & $python -m tf2onnx.convert --tflite $src --output $dst --opset 17 2>&1 | Where-Object { $_ -cmatch 'ERROR|model is saved' } | Write-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dst)) { throw "Conversion of $($pair[0]) failed." }
}

Invoke-WebRequest $licenseUrl -OutFile (Join-Path $OutDir 'LICENSE-mediapipe.txt')
@"
MediaPipe hand landmarker (Google LLC), Apache License 2.0 - see LICENSE-mediapipe.txt.
Source: $taskUrl
SHA-256 of the .task: $taskSha256
Converted to ONNX (opset 17) with $($packages -join ', ') on $(Get-Date -Format yyyy-MM-dd); no weights changed.
palm.onnx       = hand_detector.tflite (input 1x192x192x3, values 0..1)
landmarks.onnx  = hand_landmarks_detector.tflite (input 1x224x224x3, values 0..1)
"@ | Set-Content -Encoding utf8 (Join-Path $OutDir 'SOURCE.txt')

Get-ChildItem $OutDir | Format-Table Name, Length
Write-Host "Done. Hand models in $OutDir"
