<#
.SYNOPSIS
    Calls the XrealScreen local API from PowerShell (Windows PowerShell 5.1 or PowerShell 7).

.DESCRIPTION
    The running XrealScreen app listens on the named pipe \\.\pipe\XrealScreen.Api. Messages are
    JSON-RPC 2.0, one JSON object per line (docs/sdk/local-api.md). Notifications that arrive
    before a response are skipped.

.EXAMPLE
    .\samples\powershell\XrealScreenApi.ps1                                  # status
    .\samples\powershell\XrealScreenApi.ps1 -Method workspace.list
    .\samples\powershell\XrealScreenApi.ps1 -Method workspace.start -Params @{ name = 'Desk' }
    .\samples\powershell\XrealScreenApi.ps1 -Method headLook.setSettings -Params @{ mode = 'joystick'; stickSpeed = 2000 }
#>
param(
    [string]$Method = 'status.get',
    [hashtable]$Params,
    [int]$TimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'XrealScreen.Api', [System.IO.Pipes.PipeDirection]::InOut)
try {
    try {
        $pipe.Connect($TimeoutMs)
    }
    catch [TimeoutException] {
        throw 'XrealScreen is not running or its local API is off (Settings > Developers).'
    }

    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $reader = New-Object System.IO.StreamReader($pipe, $utf8)
    $writer = New-Object System.IO.StreamWriter($pipe, $utf8)
    $writer.AutoFlush = $true
    $writer.NewLine = "`n"

    function Invoke-Rpc([int]$Id, [string]$Name, $Arguments) {
        $request = [ordered]@{ jsonrpc = '2.0'; id = $Id; method = $Name }
        if ($Arguments) { $request.params = $Arguments }
        $writer.WriteLine(($request | ConvertTo-Json -Compress -Depth 5))
        while ($true) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { throw 'XrealScreen closed the connection.' }
            $message = $line | ConvertFrom-Json
            if ($message.PSObject.Properties.Name -contains 'method') { continue } # notification
            if ($message.id -ne $Id) { continue }
            if ($message.PSObject.Properties.Name -contains 'error') {
                throw "Error $($message.error.code): $($message.error.message)"
            }
            return $message.result
        }
    }

    $null = Invoke-Rpc 1 'api.hello' @{ clientName = 'PowerShell sample' }
    Invoke-Rpc 2 $Method $Params | ConvertTo-Json -Depth 5
}
finally {
    $pipe.Dispose()
}
