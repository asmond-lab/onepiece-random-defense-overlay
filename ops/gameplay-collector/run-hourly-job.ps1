[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PythonPath,
    [string]$ConfigPath,
    [ValidateRange(1, 900)]
    [int]$MaxSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$previousToken = $env:ORAND_COLLECTOR_TOKEN
$previousEndpoint = $env:ORAND_GAMEPLAY_ENDPOINT
$previousState = $env:ORAND_GAMEPLAY_STATE_DIRECTORY
$previousBytecode = $env:PYTHONDONTWRITEBYTECODE
$exitCode = 78
try {
    if (-not [System.IO.Path]::IsPathRooted($PythonPath) -or -not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) {
        throw [System.ArgumentException]::new('An absolute Python executable is required.')
    }
    if ($ConfigPath) {
        if (-not [System.IO.Path]::IsPathRooted($ConfigPath)) {
            throw [System.ArgumentException]::new('An absolute private configuration path is required.')
        }
        # Export-Clixml encrypts PSCredential with Windows DPAPI for this account/machine.
        $config = Import-Clixml -LiteralPath $ConfigPath
        $names = @($config.PSObject.Properties.Name | Sort-Object)
        if (($names -join ',') -ne 'Credential,Endpoint,StateDirectory,Version' -or
            $config.Version -ne 1 -or $config.Credential -isnot [System.Management.Automation.PSCredential]) {
            throw [System.ArgumentException]::new('Invalid private configuration schema.')
        }
        $env:ORAND_GAMEPLAY_ENDPOINT = [string]$config.Endpoint
        $env:ORAND_GAMEPLAY_STATE_DIRECTORY = [string]$config.StateDirectory
        $env:ORAND_COLLECTOR_TOKEN = $config.Credential.GetNetworkCredential().Password
    }
    $env:PYTHONDONTWRITEBYTECODE = '1'
    & $PythonPath (Join-Path $PSScriptRoot 'run_hourly_job.py') --run --max-seconds $MaxSeconds
    $exitCode = $LASTEXITCODE
}
catch {
    # Never emit exception messages, configuration, or decrypted credential values.
    $failure = @{ status = 'job-launch-failed'; exitCode = 78; errorType = $_.Exception.GetType().FullName }
    [Console]::Error.WriteLine(($failure | ConvertTo-Json -Compress))
}
finally {
    $env:ORAND_COLLECTOR_TOKEN = $previousToken
    $env:ORAND_GAMEPLAY_ENDPOINT = $previousEndpoint
    $env:ORAND_GAMEPLAY_STATE_DIRECTORY = $previousState
    $env:PYTHONDONTWRITEBYTECODE = $previousBytecode
}
exit $exitCode
