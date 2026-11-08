<#
.SYNOPSIS
    Builds the Windows archive of Atlas. It is self-contained: running it needs no .NET installation.

.DESCRIPTION
    Publishes Atlas for 64-bit Windows with its own .NET runtime, adds the license and the third-party notices,
    then writes Atlas-<version>-win-x64.zip and its SHA-256 checksum to the output folder.

.EXAMPLE
    ./build/publish.ps1
#>
param(
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
if ($props -notmatch '<Version>([^<]+)</Version>') { throw 'The version is missing from Directory.Build.props.' }
$version = $Matches[1]

$name = "Atlas-$version-win-x64"
$Output = [IO.Path]::GetFullPath($Output)
$folder = Join-Path $Output $name
$archive = Join-Path $Output "$name.zip"

New-Item -ItemType Directory -Force $Output | Out-Null
foreach ($previous in @($folder, $archive, "$archive.sha256")) {
    if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
}

dotnet publish (Join-Path $root 'src' 'Atlas.App' 'Atlas.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $folder `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $folder 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $folder
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $folder -Recurse

Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $name.zip" -Encoding ascii -NoNewline

[pscustomobject]@{
    Archive = $archive
    SizeMB = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1)
    Sha256 = $hash
}
