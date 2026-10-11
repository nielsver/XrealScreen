<#
.SYNOPSIS
  Packs the standalone virtual display driver installer (for Store installs, ADR-0005 update 2026-10-11) into
  artifacts\driver-installer\XrealScreen-Driver.zip: driver\ + Install-Driver.ps1 + README.

.DESCRIPTION
  Build the driver first (installer\driver\Build-Driver.ps1 -> artifacts\driver). Attach the zip to a GitHub release;
  the app links to releases/latest when the driver is missing (Host.VirtualDisplayDriver.DownloadUrl).
  The driver is still signed with the test certificate: a public release needs EV + Microsoft attestation signing.
#>
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$driver = Join-Path $repo 'artifacts\driver'
if (-not (Test-Path (Join-Path $driver 'VirtualDisplayDriver.cat'))) { throw 'artifacts\driver not built: run installer\driver\Build-Driver.ps1' }
$out = Join-Path $repo 'artifacts\driver-installer'
$stage = Join-Path $out 'XrealScreen-Driver'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $driver (Join-Path $stage 'driver') -Recurse
Copy-Item (Join-Path $PSScriptRoot 'Install-Driver.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'store\README-driver.md') (Join-Path $stage 'README.md')
$zip = Join-Path $out 'XrealScreen-Driver.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "driver installer: $zip"
