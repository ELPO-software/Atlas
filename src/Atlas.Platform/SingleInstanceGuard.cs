using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Atlas.Platform;

/// <summary>
/// Detects whether Atlas is already running in this user session with the same data folder, and lets a second start
/// bring the running one forward instead. The system releases the names when the process ends, even after a crash,
/// so a stale lock cannot block the next start.
/// </summary>
public sealed partial class SingleInstanceGuard : IDisposable
{
    /// <summary>The name for the default data folder.</summary>
    public const string DefaultName = @"Local\ELPO.Atlas.SingleInstance";

    /// <summary>
    /// A name every running Atlas holds, whatever its data folder. The installer waits until it is free before
    /// replacing the files of Atlas.
    /// </summary>
    public const string RunningName = @"Local\ELPO.Atlas.Running";

    private const int AnyProcess = -1;

    private readonly Mutex _mutex;
    private readonly Mutex _running;
    private readonly EventWaitHandle? _activation;
    private RegisteredWaitHandle? _listener;

    public SingleInstanceGuard(string name = DefaultName)
    {
        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        IsFirstInstance = createdNew;
        _running = new Mutex(initiallyOwned: false, RunningName);

        // Named events exist on Windows only. Elsewhere, a second start can still be detected, not forwarded.
        if (OperatingSystem.IsWindows())
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
    }

    public bool IsFirstInstance { get; }

    /// <summary>
    /// The guard name for a data folder. Each folder has its own, because the rule protects the database: two copies
    /// of Atlas may run side by side only if they never share a file.
    /// </summary>
    /// <param name="customDataDirectory">The folder named instead of the default one, or null for the default.</param>
    public static string NameFor(string? customDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(customDataDirectory))
            return DefaultName;

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(customDataDirectory)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)), 0, 8);
        return $"{DefaultName}.{hash}";
    }

    /// <summary>
    /// Calls <paramref name="onActivation"/>, on a thread-pool thread, each time another start of Atlas asks this one
    /// to come forward. A request made before this call is not lost: it is delivered as soon as listening starts.
    /// </summary>
    public void ListenForActivation(Action onActivation)
    {
        ArgumentNullException.ThrowIfNull(onActivation);
        if (!IsFirstInstance)
            throw new InvalidOperationException("Only the running instance listens for activation requests.");

        if (_activation is null || _listener is not null)
            return;

        _listener = ThreadPool.RegisterWaitForSingleObject(
            _activation, (_, _) => onActivation(), state: null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Asks the running instance to bring its window forward.</summary>
    /// <returns>False if the request could not be sent, for example on a system without named events.</returns>
    public bool ActivateFirstInstance()
    {
        if (_activation is null)
            return false;

        // The start that the user just made may move a window to the front. It hands that right to the running one,
        // which otherwise would only flash in the taskbar.
        if (OperatingSystem.IsWindows())
            AllowSetForegroundWindow(AnyProcess);

        return _activation.Set();
    }

    public void Dispose()
    {
        _listener?.Unregister(null);
        _activation?.Dispose();
        _running.Dispose();
        _mutex.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
