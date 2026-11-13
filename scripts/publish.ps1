[CmdletBinding()]
param(
    [string]$DotNet = 'dotnet',
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$dotnetExecutable = (Get-Command $DotNet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ($Iscc) { $Iscc = (Get-Command $Iscc -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source }
[xml]$properties = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Installer version must be a numeric three- or four-part version.' }
$artifacts = Join-Path $repo 'artifacts'
$publishDirectory = Join-Path $artifacts 'win-x64'

Push-Location $repo
try {
    # Only replace this script's generated directory, never an arbitrary output path or junction.
    foreach ($directory in @($artifacts, $publishDirectory)) {
        if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a redirected output directory: $directory"
        }
    }
    if (Test-Path -LiteralPath $publishDirectory) {
        $resolved = (Resolve-Path -LiteralPath $publishDirectory).Path
        if ($resolved -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/win-x64'))) {
            throw "Refusing to clean an unexpected directory: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    & $dotnetExecutable publish src/Pointa.Windows/Pointa.Windows.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false --output $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)." }
    # Skia's native symbols add ~96 MB and are only needed for native debugging.
    $skiaSymbols = Join-Path $publishDirectory 'libSkiaSharp.pdb'
    if (Test-Path -LiteralPath $skiaSymbols) { Remove-Item -LiteralPath $skiaSymbols -Force }
    foreach ($file in @('Pointa.exe', 'coreclr.dll', 'hostfxr.dll', 'Microsoft.AspNetCore.Server.Kestrel.Core.dll', 'PresentationFramework.Fluent.dll', 'PresentationCore.dll', 'SkiaSharp.dll', 'libSkiaSharp.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $file))) { throw "Incomplete self-contained publish: $file is missing." }
    }

    Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $publishDirectory
    Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination (Join-Path $publishDirectory 'docs') -Recurse
    $licenseDirectory = New-Item -ItemType Directory -Path (Join-Path $publishDirectory 'licenses') -Force
    $dotnetDirectory = Split-Path $dotnetExecutable -Parent
    foreach ($file in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        Copy-Item -LiteralPath (Join-Path $dotnetDirectory $file) -Destination (Join-Path $licenseDirectory.FullName "dotnet-$file")
    }

    $archive = Join-Path $artifacts "Pointa-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($file in @('Pointa.exe', 'coreclr.dll', 'SkiaSharp.dll', 'libSkiaSharp.dll', 'licenses/dotnet-LICENSE.txt', 'docs/licenses.md', 'docs/third-party/lan-laser-pen-LICENSE.txt', 'docs/third-party/SkiaSharp-LICENSE.txt', 'docs/third-party/SkiaSharp-THIRD-PARTY-NOTICES.txt')) {
            if (-not ($zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $file })) {
                throw "Incomplete zip: $file is missing."
            }
        }
    }
    finally { $zip.Dispose() }
    Write-Output "ZIP: $archive"

    if ($Iscc) {
        & $Iscc /Qp "/DAppVersion=$version" "/DPublishDir=$publishDirectory" "/DArtifactDir=$artifacts" (Join-Path $repo 'installer/Pointa.iss')
        if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed (exit $LASTEXITCODE)." }
        $installer = Join-Path $artifacts "Pointa-$version-Setup.exe"
        if (-not (Test-Path -LiteralPath $installer)) { throw 'ISCC did not create the expected installer.' }
        Write-Output "Setup: $installer"
    }
    else { Write-Output 'Setup was not built. Supply -Iscc with the path to Inno Setup ISCC.exe to build it.' }
}
finally { Pop-Location }
