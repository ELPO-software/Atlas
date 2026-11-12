# Helpers shared by test-installer.ps1 and test-portable.ps1. Dot-source this file.

$script:Failures = [Collections.Generic.List[string]]::new()

function Check([bool] $condition, [string] $what) {
    if ($condition) {
        Write-Host "  ok    $what"
    }
    else {
        Write-Host "  FAIL  $what" -ForegroundColor Red
        $script:Failures.Add($what)
    }
}

function Assert-NoFailure([string] $logs) {
    Write-Host ''
    if ($script:Failures.Count) { throw "$($script:Failures.Count) check(s) failed. Logs: $logs" }
    Write-Host "All checks passed. Logs: $logs" -ForegroundColor Green
}

# The machine type of a Windows binary: 0x8664 for x64, 0xAA64 for ARM64.
function Get-Machine([string] $path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $header = [BitConverter]::ToInt32($bytes, 0x3C)
    return [BitConverter]::ToUInt16($bytes, $header + 4)
}

function Wait-Until([scriptblock] $condition, [int] $seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# The window of Atlas, found by its class rather than by Process.MainWindowHandle: other software may add windows of its
# own to any process, and the first visible one is not always the one of Atlas.
if (-not ('AtlasTestWindows' -as [type])) {
    Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class AtlasTestWindows {
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    public static IntPtr Find(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) => {
            uint owner; GetWindowThreadProcessId(window, out owner);
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (owner == processId && IsWindowVisible(window) && name.ToString().StartsWith("Avalonia-")) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static bool Close(IntPtr window) { return PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
"@
}

# Starts Atlas. With a data folder, Atlas keeps away from the usual one; without .NET, it runs as on a computer where
# no .NET is installed.
function Start-Atlas([string] $exe, [string] $dataDirectory, [switch] $WithoutDotnet) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    if ($dataDirectory) { $start.Environment['ATLAS_DATA_DIR'] = $dataDirectory }
    if ($WithoutDotnet) {
        $start.Environment['PATH'] = ($env:PATH -split ';' | Where-Object { $_ -notmatch 'dotnet' }) -join ';'
        $start.Environment['DOTNET_ROOT'] = 'C:\no-dotnet-here'
        $start.Environment['DOTNET_ROOT(x86)'] = 'C:\no-dotnet-here'
    }
    return [Diagnostics.Process]::Start($start)
}

function Close-Atlas([Diagnostics.Process] $process) {
    $window = [AtlasTestWindows]::Find($process.Id)
    if ($window -ne [IntPtr]::Zero) { [AtlasTestWindows]::Close($window) | Out-Null }
    return $process.WaitForExit(15000)
}

function Get-DataFolder([string] $customDataDirectory) {
    if ($customDataDirectory) { return [IO.Path]::GetFullPath($customDataDirectory) }
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ELPO\Atlas'
}

# Starts Atlas, checks that it shows its window and opens its database, that a second start hands over to it and ends,
# and closes it. Without a custom data folder, Atlas uses its usual one.
function Test-RunningAtlas([string] $exe, [string] $customDataDirectory, [string] $when, [switch] $WithoutDotnet) {
    $atlas = Start-Atlas $exe $customDataDirectory -WithoutDotnet:$WithoutDotnet
    try {
        $database = Join-Path (Get-DataFolder $customDataDirectory) 'atlas.db'
        $started = Wait-Until { (Test-Path -LiteralPath $database) -and [AtlasTestWindows]::Find($atlas.Id) -ne [IntPtr]::Zero } 60
        Check $started "$when`: Atlas starts, shows its window and opens its database"
        Check (-not $atlas.HasExited) "$when`: Atlas keeps running"

        $second = Start-Atlas $exe $customDataDirectory -WithoutDotnet:$WithoutDotnet
        $secondEnded = $second.WaitForExit(15000)
        Check ($secondEnded -and $second.ExitCode -eq 0) "$when`: a second start hands over to the running Atlas and ends"
        Check (-not $atlas.HasExited) "$when`: the running Atlas is still there"
    }
    finally {
        if (-not $atlas.HasExited) {
            if (Close-Atlas $atlas) { Check ($atlas.ExitCode -eq 0) "$when`: Atlas closes when asked" }
            else { $atlas.Kill(); Check $false "$when`: Atlas closes when asked" }
        }
    }
}
