<#
.SYNOPSIS
    Builds the Windows downloads of Atlas. Both are self-contained: running them needs no .NET installation.

.DESCRIPTION
    Publishes Atlas for 64-bit Windows with its own .NET runtime, in two forms, each with its SHA-256 checksum:

    - Atlas-<version>-win-x64.exe, a single executable. Its license texts are built in and shown in the settings.
      On its first start, .NET extracts the native libraries it contains to %TEMP%\.net\<executable name>.
    - Atlas-<version>-win-x64.zip, a folder to extract, with LICENSE, THIRD-PARTY-NOTICES.md and the licenses folder.
      It extracts nothing at run time.

.EXAMPLE
    ./build/publish.ps1
#>
param(
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src' 'Atlas.App' 'Atlas.App.csproj'

$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
if ($props -notmatch '<Version>([^<]+)</Version>') { throw 'The version is missing from Directory.Build.props.' }
$version = $Matches[1]

$name = "Atlas-$version-win-x64"
$Output = [IO.Path]::GetFullPath($Output)
$folder = Join-Path $Output $name
$singleFileFolder = Join-Path $Output "$name-single-file"
$archive = Join-Path $Output "$name.zip"
$executable = Join-Path $Output "$name.exe"

New-Item -ItemType Directory -Force $Output | Out-Null
foreach ($previous in @($folder, $singleFileFolder, $archive, "$archive.sha256", $executable, "$executable.sha256")) {
    if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
}

function Publish([string] $destination, [string[]] $extra) {
    dotnet publish $project `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $destination `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        @extra
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

function Write-Checksum([string] $path) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$path.sha256" -Value "$hash  $(Split-Path $path -Leaf)" -Encoding ascii -NoNewline
    [pscustomobject]@{ File = $path; SizeMB = [math]::Round((Get-Item -LiteralPath $path).Length / 1MB, 1); Sha256 = $hash }
}

# The folder, in a zip archive.
Publish $folder @()
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $folder 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $folder
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $folder -Recurse
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -CompressionLevel Optimal

# The single executable.
Publish $singleFileFolder @(
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true')
$published = Get-ChildItem -LiteralPath $singleFileFolder -File
if ($published.Count -ne 1) { throw "The single-file publish produced $($published.Count) files instead of one." }
Move-Item -LiteralPath $published[0].FullName -Destination $executable
Remove-Item -LiteralPath $singleFileFolder -Recurse -Force

Write-Checksum $executable
Write-Checksum $archive
