using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Atlas.Platform.Windows.Detection;

/// <summary>
/// Reads the windows and programs of the user's session. Every call only reads: nothing here can open, change or
/// close a window or a program. A program that refuses to be read, such as one running as administrator, is skipped.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsSession
{
    private const int ExtendedStyle = -20;
    private const int ToolWindowStyle = 0x80;
    private const int OwnerWindow = 4;
    private const int CloakedAttribute = 14;
    private const int QueryLimitedInformation = 0x1000;
    private const int CommandLineInformation = 60;
    private const int InsufficientBuffer = 122;
    private const uint InfoLengthMismatch = 0xC0000004;

    public sealed record AppWindow(nint Handle, int ProcessId, string Title);

    /// <summary>The windows a user would call open applications: visible, on screen, with a title, and not tool windows.</summary>
    public static List<AppWindow> ListApplicationWindows()
    {
        var windows = new List<AppWindow>();
        var handle = nint.Zero;
        for (var guard = 0; guard < 10_000; guard++)
        {
            handle = FindWindowEx(nint.Zero, handle, null, null);
            if (handle == nint.Zero)
                break;

            if (!IsWindowVisible(handle) || GetWindow(handle, OwnerWindow) != nint.Zero)
                continue;

            if ((GetWindowLong(handle, ExtendedStyle) & ToolWindowStyle) != 0 || IsCloaked(handle))
                continue;

            var title = Title(handle);
            if (title.Length == 0)
                continue;

            GetWindowThreadProcessId(handle, out var processId);
            windows.Add(new AppWindow(handle, (int)processId, title));
        }

        return windows;
    }

    /// <summary>
    /// The process that draws inside a frame window of a Store application, whose frame belongs to
    /// ApplicationFrameHost.exe rather than to the application itself.
    /// </summary>
    public static int? HostedProcess(nint frame, int frameProcessId)
    {
        var child = nint.Zero;
        for (var guard = 0; guard < 100; guard++)
        {
            child = FindWindowEx(frame, child, "Windows.UI.Core.CoreWindow", null);
            if (child == nint.Zero)
                return null;

            GetWindowThreadProcessId(child, out var processId);
            if ((int)processId != frameProcessId)
                return (int)processId;
        }

        return null;
    }

    public static string? ImagePath(int processId) =>
        WithProcess(processId, process =>
        {
            var buffer = new char[32_768];
            var size = buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        });

    /// <summary>The application identifier of a packaged program, such as a Store application, or null for an ordinary program.</summary>
    public static string? PackagedApplicationId(int processId) =>
        WithProcess(processId, process =>
        {
            var length = 0u;
            var result = GetApplicationUserModelId(process, ref length, null);
            if (result != InsufficientBuffer || length == 0)
                return null;

            var buffer = new char[length];
            return GetApplicationUserModelId(process, ref length, buffer) == 0 ? new string(buffer, 0, (int)length - 1) : null;
        });

    /// <summary>The arguments a program was started with, split the way Windows splits them.</summary>
    public static IReadOnlyList<string>? CommandLine(int processId) =>
        WithProcess(processId, process =>
        {
            var size = 1024u;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    var status = NtQueryInformationProcess(process, CommandLineInformation, buffer, size, out var needed);
                    if (status == InfoLengthMismatch && needed > size && needed < 1_048_576)
                    {
                        size = needed;
                        continue;
                    }

                    if (status != 0)
                        return null;

                    // The buffer starts with a UNICODE_STRING whose text follows in the same buffer.
                    var length = (ushort)Marshal.ReadInt16(buffer);
                    var text = Marshal.ReadIntPtr(buffer, nint.Size);
                    return Split(Marshal.PtrToStringUni(text, length / 2));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return null;
        });

    internal static IReadOnlyList<string> Split(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return [];

        var argv = CommandLineToArgv(commandLine, out var count);
        if (argv == nint.Zero)
            return [];

        try
        {
            var arguments = new string[count];
            for (var i = 0; i < count; i++)
                arguments[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size)) ?? string.Empty;

            return arguments;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    private static T? WithProcess<T>(int processId, Func<nint, T?> read)
    {
        var process = OpenProcess(QueryLimitedInformation, false, processId);
        if (process == nint.Zero)
            return default;

        try
        {
            return read(process);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsCloaked(nint window) =>
        DwmGetWindowAttribute(window, CloakedAttribute, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string Title(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
            return string.Empty;

        var buffer = new char[Math.Min(length + 1, 2048)];
        var copied = GetWindowText(window, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied).Trim();
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindow(nint window, int command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowText(nint window, [Out] char[] text, int maxCount);

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, int flags, [Out] char[] path, ref int size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetApplicationUserModelId(nint process, ref uint length, [Out] char[]? id);

    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint NtQueryInformationProcess(nint process, int informationClass, nint information, uint length, out uint returnLength);

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CommandLineToArgv(string commandLine, out int count);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LocalFree(nint memory);
}
