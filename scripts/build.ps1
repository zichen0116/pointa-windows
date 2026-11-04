[CmdletBinding()]
param([string]$DotNet = 'dotnet')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$dotnetExecutable = (Get-Command $DotNet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

Push-Location $repo
try {
    & $dotnetExecutable build Pointa.sln --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath 'src/Pointa.Windows/bin/Release/net10.0-windows/Pointa.exe')) {
        throw 'Build succeeded without the expected Pointa.exe.'
    }
}
finally { Pop-Location }
