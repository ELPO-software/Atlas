namespace Atlas.Platform;

/// <summary>
/// Detects whether Atlas is already running in this user session. The system releases the name when the
/// process ends, even after a crash, so a stale lock cannot block the next start.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultName = @"Local\ELPO.Atlas.SingleInstance";

    private readonly Mutex _mutex;

    public SingleInstanceGuard(string name = DefaultName)
    {
        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public void Dispose() => _mutex.Dispose();
}
