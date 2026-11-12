<#
.SYNOPSIS
    Installs, starts, updates and uninstalls Atlas with a built installer, and checks each step.

.DESCRIPTION
    Runs the installer silently and checks what Windows shows afterwards: the files, the Start menu entry, the entry in
    the list of installed applications. It then starts the installed Atlas, checks that a second start goes to the
    running one, updates Atlas in place, checks that its data did not change, and uninstalls it. The data folder must
    survive the uninstallation.

    The script refuses to run if Atlas is already installed or running, so that it never changes an installation or
    data it did not create. Use -DataDirectory on a computer where Atlas holds real data.

.PARAMETER Installer
    The installer to test.

.PARAMETER Scope
    CurrentUser installs for the current account only, without administrator rights. AllUsers installs for every
    account and needs them.

.PARAMETER OlderInstaller
    An installer of an older version. When given, the test installs it first and updates it with -Installer, then
    checks that the older one does not silently replace the newer one.

.PARAMETER DataDirectory
    A data folder for the Atlas started by the test (ATLAS_DATA_DIR). By default, Atlas uses its usual folder.

.PARAMETER ExpectRefusal
    Only checks that the installer refuses to install, as it must on a processor it is not built for.
#>
param(
    [Parameter(Mandatory)] [string] $Installer,
    [ValidateSet('CurrentUser', 'AllUsers')] [string] $Scope = 'CurrentUser',
    [string] $OlderInstaller,
    [string] $DataDirectory,
    [switch] $ExpectRefusal
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'test-common.ps1')

$Installer = (Resolve-Path -LiteralPath $Installer).Path
$AppId = '{D1538AA5-DB1C-4AEB-97A7-68AC1603F0A9}_is1'
$UninstallPath = "Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
$MachineKey = "Registry::HKEY_LOCAL_MACHINE\$UninstallPath"
$UserKey = "Registry::HKEY_CURRENT_USER\$UninstallPath"
$ExpectedKey = if ($Scope -eq 'AllUsers') { $MachineKey } else { $UserKey }
$StartMenu = if ($Scope -eq 'AllUsers') { [Environment]::GetFolderPath('CommonPrograms') } else { [Environment]::GetFolderPath('Programs') }
$Shortcut = Join-Path $StartMenu 'ELPO Atlas.lnk'
$Data = Get-DataFolder $DataDirectory
$Logs = Join-Path ([IO.Path]::GetTempPath()) "atlas-installer-test-$PID"
New-Item -ItemType Directory -Force $Logs | Out-Null

function Invoke-Setup([string] $path, [string] $name) {
    $scopeSwitch = if ($Scope -eq 'AllUsers') { '/ALLUSERS' } else { '/CURRENTUSER' }
    $log = Join-Path $Logs "$name.log"
    $process = Start-Process -FilePath $path -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', $scopeSwitch, "/LOG=`"$log`"" -Wait -PassThru
    return $process.ExitCode
}

function Get-Installation {
    if (Test-Path $ExpectedKey) { return Get-ItemProperty $ExpectedKey }
    return $null
}

function Get-DataFingerprint {
    $database = Join-Path $Data 'atlas.db'
    if (Test-Path -LiteralPath $database) { return (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash }
    return $null
}

# Never change an installation or a running Atlas that this test did not create.
if ((Test-Path $MachineKey) -or (Test-Path $UserKey)) { throw 'Atlas is already installed on this computer. Uninstall it before running this test.' }
if (Get-Process -Name 'Atlas' -ErrorAction SilentlyContinue) { throw 'Atlas is running. Close it before running this test.' }

Write-Host "Installer: $Installer"
Write-Host "Scope: $Scope   Data: $Data   Processor: $env:PROCESSOR_ARCHITECTURE"

if ($ExpectRefusal) {
    $code = Invoke-Setup $Installer 'refused'
    Check ($code -ne 0) "the installer refuses to install (exit code $code)"
    Check (-not (Test-Path $MachineKey) -and -not (Test-Path $UserKey)) 'nothing is installed'
    Assert-NoFailure $Logs
    return
}

$dataBefore = Get-DataFingerprint
$expectedMachine = if ((Split-Path $Installer -Leaf) -match 'ARM64') { 0xAA64 } else { 0x8664 }

if ($OlderInstaller) {
    Check ((Invoke-Setup (Resolve-Path -LiteralPath $OlderInstaller).Path 'older') -eq 0) 'the older version installs'
    Test-RunningAtlas (Join-Path (Get-Installation).InstallLocation 'Atlas.exe') $DataDirectory 'older version'
}

# Install, or update the older version.
Check ((Invoke-Setup $Installer 'install') -eq 0) 'the installer succeeds'
$installation = Get-Installation
Check ($null -ne $installation) "Windows lists Atlas among the installed applications ($Scope)"
if ($null -eq $installation) { throw "Atlas is not installed. Logs: $Logs" }

$app = $installation.InstallLocation
$exe = Join-Path $app 'Atlas.exe'
$version = (Get-Item -LiteralPath $Installer).VersionInfo.ProductVersion.Trim([char]0, ' ')
Check ($installation.DisplayName -eq 'ELPO Atlas') "the name shown is 'ELPO Atlas' ($($installation.DisplayName))"
Check ($installation.Publisher -eq 'ELPO software') "the publisher shown is 'ELPO software' ($($installation.Publisher))"
Check ($installation.DisplayVersion -eq $version) "the version shown is $version ($($installation.DisplayVersion))"
Check ($installation.DisplayIcon -like '*\Atlas.exe') "the icon comes from Atlas.exe ($($installation.DisplayIcon))"
$expectedRoot = if ($Scope -eq 'AllUsers') { [Environment]::GetFolderPath('ProgramFiles') } else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs' }
Check ($app.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) "Atlas is installed in a standard folder ($app)"
Check (Test-Path -LiteralPath $exe) 'Atlas.exe is installed'
Check ((Get-Machine $exe) -eq $expectedMachine) ('Atlas.exe is built for the processor of this installer (0x{0:X4})' -f (Get-Machine $exe))
foreach ($file in 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'licenses\Avalonia.txt') {
    Check (Test-Path -LiteralPath (Join-Path $app $file)) "$file is installed"
}

Check (Test-Path -LiteralPath $Shortcut) "the Start menu has an entry ($Shortcut)"
if (Test-Path -LiteralPath $Shortcut) {
    $shell = New-Object -ComObject Shell.Application
    $link = $shell.NameSpace((Split-Path $Shortcut)).ParseName((Split-Path $Shortcut -Leaf))
    Check ($link.GetLink.Path -eq $exe) "the Start menu entry opens the installed Atlas ($($link.GetLink.Path))"
    Check ($link.ExtendedProperty('System.AppUserModel.ID') -eq 'ELPO.Atlas') 'the Start menu entry carries the identity Windows uses for the notifications of Atlas'
}

Test-RunningAtlas $exe $DataDirectory 'after installing'

# Setup must wait while Atlas runs, rather than replace files in use.
$running = Start-Atlas $exe $DataDirectory
Wait-Until { [AtlasTestWindows]::Find($running.Id) -ne [IntPtr]::Zero } 60 | Out-Null
Check ((Invoke-Setup $Installer 'while-running') -ne 0) 'the installer refuses to update a running Atlas'
if (-not (Close-Atlas $running)) { $running.Kill() }

# Update in place: here, install the same version again over itself.
$dataAfterUse = Get-DataFingerprint
Check ((Invoke-Setup $Installer 'update') -eq 0) 'installing again over the installed version succeeds'
Check ((Get-Installation).DisplayVersion -eq $version) 'the same installation is updated, not a second one added'
Check ((Get-DataFingerprint) -eq $dataAfterUse) 'updating does not change the data of Atlas'
Test-RunningAtlas $exe $DataDirectory 'after updating'

if ($OlderInstaller) {
    Check ((Invoke-Setup (Resolve-Path -LiteralPath $OlderInstaller).Path 'downgrade') -ne 0) 'an older version does not silently replace the newer one'
    Check ((Get-Installation).DisplayVersion -eq $version) 'the newer version stays installed'
}

# Uninstall.
$dataBeforeUninstall = Get-DataFingerprint
$uninstaller = Join-Path $app 'unins000.exe'
Check (Test-Path -LiteralPath $uninstaller) 'the uninstaller is in place'
Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $Logs 'uninstall.log')`"" -Wait
Check (Wait-Until { -not (Test-Path $ExpectedKey) } 60) 'Atlas is no longer listed among the installed applications'
Check (Wait-Until { -not (Test-Path -LiteralPath $exe) } 60) 'the application files are removed'
Check (-not (Test-Path -LiteralPath $Shortcut)) 'the Start menu entry is removed'
Check ($null -ne $dataBeforeUninstall -and (Get-DataFingerprint) -eq $dataBeforeUninstall) "the data of Atlas is kept ($Data)"
if ($DataDirectory -and $null -ne $dataBefore) {
    Check ((Get-DataFingerprint) -eq $dataBefore) 'the data folder is unchanged'
}

Assert-NoFailure $Logs
