<#
.SYNOPSIS
  Builds and signs the virtual-display-rs 0.4 driver (ADR-0008, M7 phase 2) into artifacts\driver.

.DESCRIPTION
  Upstream source (MIT, MolotovCherry/virtual-display-rs) is built unmodified at a pinned commit; only the INF
  version stamp and the signature are ours. Steps: clone/checkout -> cargo build --release -> stampinf -> sign DLL ->
  inf2cat -> sign catalog.

  Signing: a separate self-signed certificate "CN=XrealScreen Driver (test)" (created once; private key in the
  git-ignored installer\.signing\driver). A self-signed driver only installs where this certificate is in
  LocalMachine Root + TrustedPublisher, which Install-XrealScreen.ps1 adds after asking the user. Replace with an
  EV certificate + Microsoft attestation signing before a public release (ADR-0005/0008).

  Prerequisites (docs/decisions/ADR-0008): Rust via rustup (the pinned nightly is fetched automatically), the WDK
  10.0.26100, Visual Studio C++ tools, and LLVM 18 (bindgen 0.70 in this driver version produces empty structs with
  newer libclang; LLVM 23 failed [verified-local] 2026-09-27). winget ids: Rustlang.Rustup,
  Microsoft.WindowsWDK.10.0.26100, LLVM.LLVM --version 18.1.8 [verified-local 2026-10-11].
#>
param(
    [string]$Source = 'C:\GIT\third-party\virtual-display-rs',
    [string]$LibClang = '',   # default: C:\GIT\third-party\llvm-18\bin, else C:\Program Files\LLVM\bin
    [string]$DriverVersion = '0.4.0.1'
)
$ErrorActionPreference = 'Stop'
$commit = '22fcd2e0'   # crate virtual-display-driver 0.4.0 = the protocol VirtualDisplayRsProvider speaks
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$out = Join-Path $repo 'artifacts\driver'
$signingDir = Join-Path $PSScriptRoot '..\.signing\driver'
$kitBin = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0'
$subject = 'CN=XrealScreen Driver (test)'

function Invoke-Tool([string]$exe, [string[]]$arguments) {
    # Native tools write progress to stderr; with 'Stop' Windows PowerShell 5.1 would treat that as an error.
    $ErrorActionPreference = 'Continue'
    & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($exe)) failed ($LASTEXITCODE)" }
}

# 1. Source at the pinned commit
if (-not (Test-Path (Join-Path $Source '.git'))) {
    Invoke-Tool git @('clone', 'https://github.com/MolotovCherry/virtual-display-rs.git', $Source)
}
Invoke-Tool git @('-C', $Source, 'checkout', '-q', $commit)
if ((git -C $Source status --porcelain) -ne $null) { throw "$Source has local changes; the build must use unmodified upstream source" }

# 2. Build
if (-not $LibClang) {
    $LibClang = @('C:\GIT\third-party\llvm-18\bin', 'C:\Program Files\LLVM\bin') |
        Where-Object { Test-Path (Join-Path $_ 'libclang.dll') } | Select-Object -First 1
    if (-not $LibClang) { throw 'libclang.dll (LLVM 18) not found: winget install LLVM.LLVM --version 18.1.8, or pass -LibClang' }
}
$env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH"
$env:LIBCLANG_PATH = $LibClang
Push-Location (Join-Path $Source 'rust')
try { Invoke-Tool cargo @('build', '--release', '-p', 'virtual-display-driver') }
finally { Pop-Location }

# 3. Stage + stamp INF
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $Source 'rust\target\release\virtual_display_driver.dll') (Join-Path $out 'VirtualDisplayDriver.dll')
Copy-Item (Join-Path $Source 'rust\virtual-display-driver\VirtualDisplayDriver.inf') $out
Copy-Item (Join-Path $Source 'LICENSE') (Join-Path $out 'LICENSE-virtual-display-rs.txt')   # MIT: ship the notice
Invoke-Tool "$kitBin\x86\stampinf.exe" @('-v', $DriverVersion, '-d', '*', '-a', 'amd64', '-u', '2.15.0', '-f', (Join-Path $out 'VirtualDisplayDriver.inf'))

# 4. Driver certificate (created once, reused)
New-Item -ItemType Directory -Force $signingDir | Out-Null
$pfx = Join-Path $signingDir 'XrealScreen-driver.pfx'
$pwdFile = Join-Path $signingDir 'pfx-password.txt'
if (-not (Test-Path $pfx)) {
    $password = [Guid]::NewGuid().ToString('N')
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -FriendlyName 'XrealScreen driver test signing' `
        -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(3)
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
    Set-Content -Path $pwdFile -Value $password -NoNewline
    Write-Host "created driver signing certificate $($cert.Thumbprint)"
}
$password = Get-Content $pwdFile -Raw
$certObj = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $password)
[IO.File]::WriteAllBytes((Join-Path $out 'XrealScreenDriver.cer'), $certObj.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))

# 5. Sign DLL, build + sign catalog
$sign = @('sign', '/fd', 'SHA256', '/f', $pfx, '/p', $password, '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
Invoke-Tool "$kitBin\x64\signtool.exe" ($sign + (Join-Path $out 'VirtualDisplayDriver.dll'))
Invoke-Tool "$kitBin\x86\Inf2Cat.exe" @("/driver:$out", '/os:10_x64,10_VB_X64,10_CO_X64,10_NI_X64')
Invoke-Tool "$kitBin\x64\signtool.exe" ($sign + (Join-Path $out 'VirtualDisplayDriver.cat'))

Write-Host "driver package: $out (version $DriverVersion, signer $subject)"
Get-ChildItem $out | Format-Table Name, Length -AutoSize
