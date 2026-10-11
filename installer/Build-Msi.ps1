<#
.SYNOPSIS
  Builds XrealScreen-<version>-x64.msi with FalkForge (ADR-0013) into artifacts\installer.

.DESCRIPTION
  1. Publishes the app unpackaged and self-contained (.NET + Windows App SDK inside; WindowsPackageType=None) and
     the xrs tool into one payload folder (artifacts\msi\payload).
  2. Bundles the virtual display driver from artifacts\driver when it has been built
     (installer\driver\Build-Driver.ps1); without it the MSI has no Driver feature.
  3. Optional signing (-CertificateThumbprint, cert in CurrentUser\My): signs our exes/DLLs with signtool and the
     MSI through FalkForge. Unsigned builds are fine for testing; a public release needs a trusted certificate.
  4. Builds the MSI (installer\msi, FalkForge) and validates it with ICE (FalkForge CLI from dotnet-tools.json).

.EXAMPLE
  .\installer\Build-Msi.ps1
  .\installer\Build-Msi.ps1 -Version 0.2.0
  msiexec /i artifacts\installer\XrealScreen-0.1.0-x64.msi /l*v install.log
#>
param(
    [string]$Version = '0.1.0',
    [string]$Configuration = 'Release',
    [string]$CertificateThumbprint = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$payload = Join-Path $repo 'artifacts\msi\payload'
$out = Join-Path $repo 'artifacts\installer'
$msi = Join-Path $out "XrealScreen-$Version-x64.msi"

function Invoke-Checked([scriptblock]$Command, [string]$What) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed ($LASTEXITCODE)" }
}

Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payload, $out | Out-Null

# 1. Payload: xrs first, then the app (the app's copies of shared assemblies win; same central versions anyway)
Invoke-Checked { dotnet publish (Join-Path $repo 'tools\XrealScreen.Cli') -c $Configuration -r win-x64 --self-contained `
    -p:Version=$Version -o $payload } 'publish xrs'
Invoke-Checked { dotnet publish (Join-Path $repo 'src\XrealScreen.App') -c $Configuration -p:Platform=x64 -r win-x64 `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:Version=$Version `
    -o $payload } 'publish app'
Get-ChildItem $payload -Filter *.pdb | Remove-Item
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $payload 'LICENSE.txt')
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $payload

# 2. Driver (optional)
$driver = Join-Path $repo 'artifacts\driver'
if (Test-Path (Join-Path $driver 'VirtualDisplayDriver.inf')) {
    Copy-Item $driver (Join-Path $payload 'driver') -Recurse
    Write-Host 'driver: bundled from artifacts\driver'
} else {
    Write-Warning 'driver: artifacts\driver not built (installer\driver\Build-Driver.ps1) - MSI has no Driver feature'
}

# 3. Signing (optional, later: ADR-0013)
if ($CertificateThumbprint) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK)' }
    $ours = Get-ChildItem $payload -File | Where-Object { $_.Name -like 'XrealScreen*' -or $_.Name -eq 'xrs.exe' -or $_.Name -eq 'xrs.dll' }
    Invoke-Checked { & $signtool.FullName sign /sha1 $CertificateThumbprint /fd sha256 /tr http://timestamp.digicert.com /td sha256 $ours.FullName } 'signtool'
    $env:XRS_SIGN_THUMBPRINT = $CertificateThumbprint
}

# 4. MSI
try {
    # -o is a folder; FalkForge names the file <Name>-<Version>.msi
    $build = Join-Path $repo 'artifacts\msi\out'
    Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue
    Invoke-Checked { dotnet run --project (Join-Path $PSScriptRoot 'msi') -c $Configuration -- --payload $payload --license (Join-Path $repo 'LICENSE') --version $Version -o $build } 'FalkForge build'
    Move-Item (Get-ChildItem $build -Filter *.msi | Select-Object -First 1).FullName $msi -Force
} finally {
    Remove-Item Env:XRS_SIGN_THUMBPRINT -ErrorAction SilentlyContinue
}

# ICE validation with the FalkForge CLI pinned in dotnet-tools.json
Push-Location $repo
try {
    Invoke-Checked { dotnet tool restore } 'dotnet tool restore'
    # darice.cub comes with the Windows SDK MSI tools; without it ICE is skipped (FalkForge prints why)
    Invoke-Checked { dotnet forge validate $msi --ice --ice-skip-when-cub-unavailable } 'forge validate'
} finally { Pop-Location }
Write-Host "msi: $msi  ($([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB)" -ForegroundColor Green
