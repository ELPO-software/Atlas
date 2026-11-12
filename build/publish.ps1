<#
.SYNOPSIS
    Builds the Windows downloads of Atlas: an installer and a portable archive for each architecture.

.DESCRIPTION
    For each architecture, publishes Atlas with its own .NET runtime, so that running it needs no .NET installation,
    checks that every binary is built for that architecture, and writes to the output folder:

    - Atlas-Setup-x64.exe and Atlas-Setup-ARM64.exe, the installers, built by Inno Setup 6.7 from
      build/installer/Atlas.iss;
    - Atlas-Portable-x64.zip and Atlas-Portable-ARM64.zip, the same application in a folder to extract, which installs
      nothing;
    - SHA256SUMS.txt, the SHA-256 checksum of each of them.

    Each download carries LICENSE.txt, THIRD-PARTY-NOTICES.md and the licenses folder.

    Inno Setup is looked for in the ISCC environment variable, then on the PATH, then in its usual folders.

.PARAMETER Architectures
    The architectures to build: x64, arm64, or both (the default).

.PARAMETER Output
    The folder to write to. Defaults to the artifacts folder of the repository.

.EXAMPLE
    ./build/publish.ps1

.EXAMPLE
    ./build/publish.ps1 -Architectures arm64
#>
param(
    [ValidateSet('x64', 'arm64')]
    [string[]] $Architectures = @('x64', 'arm64'),

    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src' 'Atlas.App' 'Atlas.App.csproj'
$installerScript = Join-Path $PSScriptRoot 'installer' 'Atlas.iss'

$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
if ($props -notmatch '<Version>((\d+)\.(\d+)\.(\d+)[^<]*)</Version>') { throw 'The version is missing from Directory.Build.props.' }
$version = $Matches[1]
$versionParts = @($Matches[2], $Matches[3], $Matches[4])

# The names used in file names, and the machine type every native binary must have.
$label = @{ x64 = 'x64'; arm64 = 'ARM64' }
$expectedMachine = @{ x64 = 0x8664; arm64 = 0xAA64 }

function Find-InnoSetupCompiler {
    $candidates = @(
        $env:ISCC,
        (Get-Command 'iscc.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6' 'ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6' 'ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs' 'Inno Setup 6' 'ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    throw 'Inno Setup 6.7 was not found. Install it from https://jrsoftware.org/isdl.php, or set ISCC to the path of ISCC.exe.'
}

# Reads the machine type of a Windows binary, and whether it is a .NET assembly that runs on any processor.
function Get-BinaryKind([string] $path) {
    $stream = [IO.File]::OpenRead($path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { return $null }
        $stream.Position = 0x3C
        $header = $reader.ReadInt32()
        $stream.Position = $header
        if ($reader.ReadUInt32() -ne 0x4550) { return $null }
        $machine = $reader.ReadUInt16()
        $sectionCount = $reader.ReadUInt16()
        $stream.Position = $header + 20
        $optionalSize = $reader.ReadUInt16()
        $stream.Position = $header + 24
        $dataDirectories = $header + 24 + $(if ($reader.ReadUInt16() -eq 0x20B) { 112 } else { 96 })
        $stream.Position = $dataDirectories + 14 * 8
        $cliHeader = $reader.ReadUInt32()

        $anyCpu = $false
        if ($cliHeader -ne 0 -and $machine -eq 0x14C) {
            for ($i = 0; $i -lt $sectionCount; $i++) {
                $stream.Position = $header + 24 + $optionalSize + $i * 40 + 8
                $virtualSize = $reader.ReadUInt32(); $virtualAddress = $reader.ReadUInt32()
                $rawSize = $reader.ReadUInt32(); $rawPointer = $reader.ReadUInt32()
                if ($cliHeader -ge $virtualAddress -and $cliHeader -lt $virtualAddress + [Math]::Max($virtualSize, $rawSize)) {
                    $stream.Position = $rawPointer + ($cliHeader - $virtualAddress) + 16
                    $flags = $reader.ReadUInt32()
                    # IL only, and not limited to 32-bit processes.
                    $anyCpu = ($flags -band 1) -and -not ($flags -band 2)
                    break
                }
            }
        }
        return [pscustomobject]@{ Machine = $machine; AnyCpu = $anyCpu }
    }
    finally {
        $stream.Dispose()
    }
}

# A binary built for another processor would fail to load, or run under emulation without anyone knowing.
function Assert-Architecture([string] $folder, [string] $architecture) {
    $wrong = foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Include '*.dll', '*.exe') {
        $kind = Get-BinaryKind $file.FullName
        if ($null -ne $kind -and -not $kind.AnyCpu -and $kind.Machine -ne $expectedMachine[$architecture]) {
            '{0} (machine 0x{1:X4})' -f $file.Name, $kind.Machine
        }
    }
    if ($wrong) { throw "These files are not built for $architecture`: $($wrong -join ', ')" }
}

function Write-Archive([string] $folder, [string] $archive) {
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -CompressionLevel Optimal
}

$Output = [IO.Path]::GetFullPath($Output)
$publishRoot = Join-Path $Output 'publish'
New-Item -ItemType Directory -Force $Output | Out-Null
$iscc = Find-InnoSetupCompiler

$downloads = foreach ($architecture in $Architectures) {
    $folder = Join-Path $publishRoot "win-$architecture"
    $installer = Join-Path $Output "Atlas-Setup-$($label[$architecture]).exe"
    $archive = Join-Path $Output "Atlas-Portable-$($label[$architecture]).zip"
    foreach ($previous in @($folder, $installer, $archive)) {
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    }

    dotnet publish $project `
        --configuration Release `
        --runtime "win-$architecture" `
        --self-contained true `
        --output $folder `
        -p:DebugType=None `
        -p:DebugSymbols=false |
        Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $architecture with exit code $LASTEXITCODE." }

    Assert-Architecture $folder $architecture

    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $folder 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $folder
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $folder -Recurse

    Write-Archive $folder $archive

    & $iscc /Qp `
        "/DAppVersion=$version" `
        "/DVersionMajor=$($versionParts[0])" `
        "/DVersionMinor=$($versionParts[1])" `
        "/DVersionPatch=$($versionParts[2])" `
        "/DArch=$architecture" `
        "/DSourceDir=$folder" `
        "/DOutputDir=$Output" `
        "/DOutputName=Atlas-Setup-$($label[$architecture])" `
        $installerScript |
        Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for $architecture with exit code $LASTEXITCODE." }

    $installer
    $archive
}

$sums = foreach ($file in $downloads) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    [pscustomobject]@{ File = Split-Path $file -Leaf; SizeMB = [math]::Round((Get-Item -LiteralPath $file).Length / 1MB, 1); Sha256 = $hash }
}

$sumsFile = Join-Path $Output 'SHA256SUMS.txt'
Set-Content -LiteralPath $sumsFile -Encoding ascii -Value ($sums | ForEach-Object { "$($_.Sha256)  $($_.File)" })
$sums
