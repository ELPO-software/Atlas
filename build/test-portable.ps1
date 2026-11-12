<#
.SYNOPSIS
    Extracts a portable archive of Atlas and checks that it runs without any .NET installation.

.DESCRIPTION
    Extracts the archive into a folder whose name has spaces and accents, checks its checksum against SHA256SUMS.txt
    when it is next to the archive, checks that every binary is built for the processor of the archive, then starts
    Atlas with .NET hidden from its environment and a data folder of its own. The extracted copy is removed at the end.

.PARAMETER Archive
    The portable archive to test.
#>
param(
    [Parameter(Mandatory)] [string] $Archive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'test-common.ps1')

$Archive = (Resolve-Path -LiteralPath $Archive).Path
$work = Join-Path ([IO.Path]::GetTempPath()) "atlas-portable-test-$PID"
$folder = Join-Path $work "Copie d'essai d'Atlas"
$data = Join-Path $work 'données'
New-Item -ItemType Directory -Force $folder | Out-Null

Write-Host "Archive: $Archive   Processor: $env:PROCESSOR_ARCHITECTURE"

$sums = Join-Path (Split-Path $Archive) 'SHA256SUMS.txt'
if (Test-Path -LiteralPath $sums) {
    $expected = Get-Content -LiteralPath $sums | Where-Object { $_ -match "  $([regex]::Escape((Split-Path $Archive -Leaf)))$" } | ForEach-Object { ($_ -split ' ')[0] }
    Check ($expected -eq (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()) 'the archive matches its published checksum'
}

Expand-Archive -LiteralPath $Archive -DestinationPath $folder
$exe = Join-Path $folder 'Atlas.exe'
Check (Test-Path -LiteralPath $exe) 'Atlas.exe is in the archive'

$expectedMachine = if ((Split-Path $Archive -Leaf) -match 'ARM64') { 0xAA64 } else { 0x8664 }
Check ((Get-Machine $exe) -eq $expectedMachine) ('Atlas.exe is built for the processor of this archive (0x{0:X4})' -f (Get-Machine $exe))
foreach ($native in 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'e_sqlite3.dll', 'av_libglesv2.dll', 'coreclr.dll') {
    $path = Join-Path $folder $native
    Check ((Test-Path -LiteralPath $path) -and (Get-Machine $path) -eq $expectedMachine) "$native is built for the same processor"
}
foreach ($file in 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'licenses\Avalonia.txt') {
    Check (Test-Path -LiteralPath (Join-Path $folder $file)) "$file is in the archive"
}

$runsHere = ($expectedMachine -eq 0xAA64) -eq ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64')
if ($runsHere) {
    Test-RunningAtlas $exe $data 'portable copy' -WithoutDotnet
}
else {
    Write-Host "  skip  starting Atlas: this computer has another processor ($env:PROCESSOR_ARCHITECTURE)"
}

Remove-Item -LiteralPath $work -Recurse -Force
Assert-NoFailure $work
