<#
.SYNOPSIS
  Builds the Microsoft Store package of XrealScreen: an unsigned .msixbundle (x64 + ARM64) in artifacts\store.

.DESCRIPTION
  - Identity comes from installer\store\StoreIdentity.json (Partner Center -> Product identity). The Store signs
    the package itself, so it is built unsigned. With placeholder values the script still builds (with the
    sideload identity) so the build and WACK can be tried, but the result cannot be uploaded.
  - The virtual display driver is NOT in the Store package (MSIX cannot install drivers, ADR-0005 update
    2026-10-11). Without the driver the app shows how to get it; head look and the local API work without it.
  - Self-contained (.NET + Windows App SDK inside), like the sideload package (installer\Build-Package.ps1).
  - The Store requires the last version field to be 0 (e.g. 1.2.0.0).
  - -RunWack (administrator): signs a copy with a local test certificate whose subject equals the manifest
    Publisher, trusts it (LocalMachine\TrustedPeople) and runs the Windows App Certification Kit on it.
#>
param(
    [string]$Version = '',
    [string[]]$Platforms = @('x64', 'arm64'),
    [string]$Configuration = 'Release',
    [switch]$RunWack
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $repo 'src\XrealScreen.App\XrealScreen.App.csproj'
$manifest = Join-Path $repo 'src\XrealScreen.App\Package.appxmanifest'
$out = Join-Path $repo 'artifacts\store'
$identity = Get-Content (Join-Path $PSScriptRoot 'store\StoreIdentity.json') -Raw | ConvertFrom-Json

$originalManifest = Get-Content $manifest -Raw
$isPlaceholder = @($identity.IdentityName, $identity.Publisher, $identity.PublisherDisplayName) | Where-Object { -not $_ -or $_.StartsWith('<') }
if ($isPlaceholder) {
    Write-Warning 'installer\store\StoreIdentity.json has placeholders: building with the sideload identity (NOT uploadable to the Store).'
}

if (-not $Version) {
    $Version = [regex]::Match($originalManifest, '<Identity[^>]*Version="([^"]+)"').Groups[1].Value
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version '$Version': the Store needs four numbers ending in .0 (e.g. 1.2.0.0)" }

# Windows SDK tools (newest installed version)
$sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'makeappx.exe not found (install the Windows SDK)' }
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'

# 1. Stamp identity + version into the manifest (restored afterwards)
$stamped = $originalManifest -replace '(<Identity[^>]*Version=")[^"]+(")', "`${1}$Version`$2"
if (-not $isPlaceholder) {
    $stamped = $stamped -replace '(<Identity[^>]*Name=")[^"]+(")', "`${1}$($identity.IdentityName)`$2"
    $stamped = $stamped -replace '(<Identity[^>]*Publisher=")[^"]+(")', "`${1}$($identity.Publisher)`$2"
    $stamped = $stamped -replace '<PublisherDisplayName>[^<]*</PublisherDisplayName>', "<PublisherDisplayName>$([Security.SecurityElement]::Escape($identity.PublisherDisplayName))</PublisherDisplayName>"
}
$publisher = [regex]::Match($stamped, '<Identity[^>]*Publisher="([^"]+)"').Groups[1].Value

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
$bundleInput = Join-Path $out 'bundle-input'
New-Item -ItemType Directory -Force $bundleInput | Out-Null

# 2. One unsigned .msix per platform
try {
    Set-Content $manifest -Value $stamped -NoNewline -Encoding utf8
    foreach ($platform in $Platforms) {
        $pkgDir = Join-Path $out "msix-$platform\"
        dotnet publish $project -c $Configuration -p:Platform=$platform -r "win-$platform" `
            -p:GenerateAppxPackageOnBuild=true -p:AppxPackageDir=$pkgDir -p:AppxBundle=Never `
            -p:UapAppxPackageBuildMode=StoreUpload -p:AppxPackageSigningEnabled=false `
            -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:AppxSymbolPackageEnabled=false
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($platform) failed ($LASTEXITCODE)" }
        $msix = Get-ChildItem $pkgDir -Recurse -Filter *.msix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $msix) { throw "no .msix produced for $platform" }
        Copy-Item $msix.FullName (Join-Path $bundleInput "XrealScreen_$($Version)_$platform.msix")
    }
}
finally {
    Set-Content $manifest -Value $originalManifest -NoNewline -Encoding utf8
}

# 3. Bundle (what you upload in Partner Center -> Packages)
$bundle = Join-Path $out "XrealScreen_$Version.msixbundle"
& $makeappx bundle /o /d $bundleInput /p $bundle /bv $Version | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed ($LASTEXITCODE)" }
Write-Host "store bundle: $bundle  ($([math]::Round((Get-Item $bundle).Length / 1MB, 1)) MB)  platforms: $($Platforms -join ', ')  publisher: $publisher"
if ($isPlaceholder) { Write-Warning 'placeholder identity: fill in installer\store\StoreIdentity.json before uploading.' }

# 4. Optional: Windows App Certification Kit on a locally signed copy
if ($RunWack) {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw '-RunWack needs an administrator PowerShell' }
    $appcert = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe'
    if (-not (Test-Path $appcert)) { throw 'Windows App Certification Kit not found (Windows SDK feature)' }

    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq 'XrealScreen WACK test' } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -FriendlyName 'XrealScreen WACK test' `
            -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(1) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    $cer = Join-Path $out 'wack-test.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    $signed = Join-Path $out 'wack\XrealScreen-wack.msixbundle'
    New-Item -ItemType Directory -Force (Split-Path $signed) | Out-Null
    Copy-Item $bundle $signed -Force
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $signed | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }

    $report = Join-Path $out 'wack\report.xml'
    & $appcert reset | Out-Null
    & $appcert test -appxpackagepath $signed -reportoutputpath $report
    [xml]$xml = Get-Content $report
    Write-Host "WACK: $($xml.REPORT.OVERALL_RESULT)  report: $report"
    $xml.REPORT.REQUIREMENTS.REQUIREMENT.TEST | Where-Object { $_.RESULT.'#cdata-section' -eq 'FAIL' } |
        ForEach-Object { Write-Warning "FAIL: $($_.NAME)" }
}
