[CmdletBinding()]
param([string]$DotNet = 'dotnet')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$dotnetExecutable = (Get-Command $DotNet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

Push-Location $repo
try {
    & $dotnetExecutable run --project tests/Pointa.SelfTest/Pointa.SelfTest.csproj --configuration Release --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Self-tests failed (exit $LASTEXITCODE)." }
}
finally { Pop-Location }
