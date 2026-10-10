<#
.SYNOPSIS
    Builds the XrealScreen SDK NuGet packages into artifacts\nuget (ADR-0012, docs/sdk).

.DESCRIPTION
    Packages: XrealScreen.Sdk (+ its dependencies XrealScreen.Core, XrealScreen.Device.XrealOne,
    XrealScreen.Display), XrealScreen.Contracts and XrealScreen.Sdk.Client. Runs the SDK and API
    tests first. Use the folder as a local NuGet source:
        dotnet nuget add source <repo>\artifacts\nuget --name XrealScreenLocal

.EXAMPLE
    .\tools\Pack-Sdk.ps1
    .\tools\Pack-Sdk.ps1 -Version 0.2.0 -SkipTests
#>
param(
    [string]$Version = '',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\nuget'
$projects = @(
    'src\XrealScreen.Core\XrealScreen.Core.csproj',
    'src\XrealScreen.Device.XrealOne\XrealScreen.Device.XrealOne.csproj',
    'src\XrealScreen.Display\XrealScreen.Display.csproj',
    'src\XrealScreen.Sdk\XrealScreen.Sdk.csproj',
    'src\XrealScreen.Contracts\XrealScreen.Contracts.csproj',
    'src\XrealScreen.Sdk.Client\XrealScreen.Sdk.Client.csproj'
)

if (-not $SkipTests) {
    foreach ($test in 'tests\XrealScreen.Sdk.Tests', 'tests\XrealScreen.Api.Tests', 'tests\XrealScreen.Core.Tests') {
        dotnet test --project (Join-Path $root $test) -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $test" }
    }
}

New-Item -ItemType Directory -Force $out | Out-Null
$versionArgs = if ($Version) { @("-p:Version=$Version") } else { @() }
foreach ($project in $projects) {
    dotnet pack (Join-Path $root $project) -c Release -o $out @versionArgs
    if ($LASTEXITCODE -ne 0) { throw "Pack failed: $project" }
}

Get-ChildItem $out -Filter *.nupkg | Sort-Object Name | ForEach-Object { "  $($_.Name)" }
"Packages in $out"
