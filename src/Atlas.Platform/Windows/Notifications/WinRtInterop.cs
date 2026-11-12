using System.Runtime.InteropServices;
using System.Runtime.Versioning;

#pragma warning disable SYSLIB1096 // The few Windows Runtime interfaces Atlas uses are declared by hand, for built-in COM.

namespace Atlas.Platform.Windows.Notifications;

/// <summary>
/// The minimum of the Windows Runtime that notifications need, declared by hand from the Windows metadata
/// (Windows.UI.winmd and Windows.Data.winmd). Each interface lists its methods in the order of the metadata, which is
/// the order of the table Windows calls through: the first three stand for those of IInspectable.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WinRt
{
    public static T Factory<T>(string className, Guid interfaceId)
    {
        using var name = new WindowsString(className);
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(name.Handle, interfaceId, out var factory));
        try
        {
            return (T)Marshal.GetObjectForIUnknown(factory);
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    /// <returns>A reference the caller must release.</returns>
    public static nint Activate(string className)
    {
        using var name = new WindowsString(className);
        Marshal.ThrowExceptionForHR(RoActivateInstance(name.Handle, out var instance));
        return instance;
    }

    /// <returns>A reference the caller must release.</returns>
    public static nint QueryInterface(nint instance, Guid interfaceId)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(instance, in interfaceId, out var result));
        return result;
    }

    [LibraryImport("combase.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RoGetActivationFactory(nint classId, in Guid interfaceId, out nint factory);

    [LibraryImport("combase.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RoActivateInstance(nint classId, out nint instance);
}

/// <summary>A Windows Runtime string (HSTRING), released when disposed.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsString : IDisposable
{
    public WindowsString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, value.Length, out var handle));
        Handle = handle;
    }

    public nint Handle { get; private set; }

    /// <summary>Reads a string the Windows Runtime handed over, and releases it.</summary>
    public static string Read(nint handle)
    {
        if (handle == nint.Zero)
            return string.Empty;

        try
        {
            var buffer = WindowsGetStringRawBuffer(handle, out var length);
            return Marshal.PtrToStringUni(buffer, (int)length);
        }
        finally
        {
            _ = WindowsDeleteString(handle);
        }
    }

    public void Dispose()
    {
        if (Handle != nint.Zero)
        {
            _ = WindowsDeleteString(Handle);
            Handle = nint.Zero;
        }
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WindowsCreateString(string text, int length, out nint handle);

    [LibraryImport("combase.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WindowsDeleteString(nint handle);

    [LibraryImport("combase.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WindowsGetStringRawBuffer(nint handle, out uint length);
}

[ComImport]
[Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotificationManagerStatics
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    IToastNotifier CreateToastNotifier();

    IToastNotifier CreateToastNotifierWithId(nint applicationId);
}

[ComImport]
[Guid("7AB93C52-0E48-4750-BA9D-1A4113981847")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotificationManagerStatics2
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    IToastNotificationHistory GetHistory();
}

[ComImport]
[Guid("5CADDC63-01D3-4C97-986F-0533483FEE14")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotificationHistory
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void RemoveGroup(nint group);

    void RemoveGroupWithId(nint group, nint applicationId);

    void RemoveGroupedTagWithId(nint tag, nint group, nint applicationId);

    void RemoveGroupedTag(nint tag, nint group);

    void Remove(nint tag);

    void Clear();

    void ClearWithId(nint applicationId);
}

[ComImport]
[Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotifier
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void Show(IToastNotification notification);

    void Hide(IToastNotification notification);

    int GetSetting();
}

[ComImport]
[Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotificationFactory
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    IToastNotification CreateToastNotification(nint content);
}

[ComImport]
[Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotification
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    nint GetContent();

    void SetExpirationTime(nint value);

    nint GetExpirationTime();

    long AddDismissed(nint handler);

    void RemoveDismissed(long token);

    long AddActivated(IToastActivatedHandler handler);

    void RemoveActivated(long token);
}

[ComImport]
[Guid("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotification2
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void SetTag(nint value);

    nint GetTag();

    void SetGroup(nint value);
}

[ComImport]
[Guid("E3BF92F3-C197-436F-8265-0625824F8DAC")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastActivatedEventArgs
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    nint GetArguments();
}

[ComImport]
[Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IXmlDocumentIO
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void LoadXml(nint xml);
}

/// <summary>Only its identifier is used: the notification factory takes the document through this interface.</summary>
[ComImport]
[Guid("F7F3A506-1E87-42D6-BCFB-B8C809FA5494")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IXmlDocument
{
}

/// <summary>
/// TypedEventHandler&lt;ToastNotification, object&gt;. A delegate derives from IUnknown only. Its identifier is computed
/// from the generic definition and its arguments, as the Windows Runtime specifies; WinRtInteropTests recomputes it.
/// </summary>
[ComImport]
[Guid("AB54DE2D-97D9-5528-B6AD-105AFE156530")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastActivatedHandler
{
    void Invoke(nint sender, nint args);
}
