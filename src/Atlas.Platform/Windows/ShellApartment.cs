using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace Atlas.Platform.Windows;

/// <summary>
/// One long-lived single-threaded apartment for every use of the shell's scripting objects (Shell.Application).
/// </summary>
/// <remarks>
/// The shell may hand the same object to several threads. When a thread that used it ends, Windows detaches the object
/// for all of them, and another thread still reading it fails. Running every read on this one thread, which never
/// ends, avoids that, and keeps the reads from overlapping.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class ShellApartment
{
    private static readonly BlockingCollection<Action> Work = [];
    private static readonly Lazy<Thread> Worker = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = Worker.Value;

        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try
            {
                result.SetResult(work());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        });
        return result.Task;
    }

    private static Thread Start()
    {
        var thread = new Thread(() =>
        {
            // Waiting here pumps the messages COM needs, as every wait on a single-threaded apartment does.
            foreach (var work in Work.GetConsumingEnumerable())
                work();
        })
        {
            IsBackground = true,
            Name = "Atlas shell",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }
}
