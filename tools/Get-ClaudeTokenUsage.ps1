<#
.SYNOPSIS
    Sums Claude Code token usage for this repo over a time window (for the token log in docs/ROADMAP.md).

.DESCRIPTION
    Reads the Claude Code session logs (~\.claude\projects\<repo slug>\**\*.jsonl, subagents included),
    keeps assistant messages whose timestamp is inside [-Since, -Until), counts each message id once
    (streamed messages are logged several times) and prints the totals per model.
    "Input" is uncached input; "Cache write" and "Cache read" are the prompt-cache parts of the input.

.EXAMPLE
    .\tools\Get-ClaudeTokenUsage.ps1 -Since '2026-10-29 19:20' -Until '2026-10-29 21:00'
    .\tools\Get-ClaudeTokenUsage.ps1 -Since (Get-Date).AddHours(-2) -Session 5366a2fc-5cb2-4426-9edf-21fa9941ff42
#>
param(
    [Parameter(Mandatory)][datetime]$Since,
    [datetime]$Until = [datetime]::MaxValue,
    [string]$Session = '',
    [string]$LogDir = ''
)

$ErrorActionPreference = 'Stop'

if (-not $LogDir) {
    # Claude Code names the folder after the repo path: drive letter lower case, ':' '\' '/' -> '-'.
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $slug = ($root.Substring(0, 1).ToLowerInvariant() + $root.Substring(1)) -replace '[:\\/ ]', '-'
    $LogDir = Join-Path $HOME ".claude\projects\$slug"
}
if (-not (Test-Path $LogDir)) { throw "No Claude Code logs at $LogDir (pass -LogDir)." }

$sinceUtc = $Since.ToUniversalTime()
$untilUtc = if ($Until -eq [datetime]::MaxValue) { $Until } else { $Until.ToUniversalTime() }
$files = Get-ChildItem $LogDir -Recurse -Filter *.jsonl | Where-Object { $_.LastWriteTimeUtc -ge $sinceUtc }
if ($Session) { $files = $files | Where-Object { $_.FullName -match [regex]::Escape($Session) } }

$seen = @{}
$rows = foreach ($file in $files) {
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if ($line -notmatch '"usage"') { continue }
        $entry = $line | ConvertFrom-Json
        $msg = $entry.message
        if (-not $msg.usage -or -not $entry.timestamp) { continue }
        $time = ([datetime]$entry.timestamp).ToUniversalTime()
        if ($time -lt $sinceUtc -or $time -ge $untilUtc) { continue }
        if ($seen.ContainsKey($msg.id)) { continue }
        $seen[$msg.id] = $true
        [pscustomobject]@{
            Model      = $msg.model
            Input      = [long]$msg.usage.input_tokens
            CacheWrite = [long]$msg.usage.cache_creation_input_tokens
            CacheRead  = [long]$msg.usage.cache_read_input_tokens
            Output     = [long]$msg.usage.output_tokens
        }
    }
}

if (-not $rows) { Write-Host 'No usage in that window.'; return }

$rows | Group-Object Model | ForEach-Object {
    [pscustomobject]@{
        Model        = $_.Name
        Requests     = $_.Count
        Input        = [long]($_.Group | Measure-Object Input -Sum).Sum
        'Cache write' = [long]($_.Group | Measure-Object CacheWrite -Sum).Sum
        'Cache read'  = [long]($_.Group | Measure-Object CacheRead -Sum).Sum
        Output       = [long]($_.Group | Measure-Object Output -Sum).Sum
    }
} | Format-Table -AutoSize
