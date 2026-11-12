using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Atlas.Core.Notifications;

namespace Atlas.Platform.Windows.Notifications;

/// <summary>
/// Shows Windows notifications for Atlas through the Windows Runtime, without any additional library.
/// Windows attributes them to the Start menu entry the installer creates, which carries <see cref="AppId"/>: a copy of
/// Atlas that was not installed has no such entry, and then reports that notifications are not available.
/// </summary>
/// <remarks>
/// Atlas answers a notification only while it runs. It removes its notifications when it closes, so that none is left
/// in the notification center that nothing would answer.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsNotifications : IDesktopNotifications
{
    /// <summary>The application identity of Atlas, also written on its Start menu entry by the installer.</summary>
    public const string AppId = "ELPO.Atlas";

    private const string Group = "atlas";
    private const int NotFound = unchecked((int)0x80070490);

    private readonly string _appId;
    private readonly bool _isInstalledCopy;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ShownNotification> _shown = new(StringComparer.Ordinal);
    private IToastNotifier? _notifier;

    /// <param name="isInstalledCopy">
    /// Whether this copy of Atlas is the installed one (<see cref="InstalledCopy"/>). Any other copy never notifies.
    /// </param>
    /// <param name="appId">The identity to notify under. Tests use one Windows does not know.</param>
    public WindowsNotifications(bool isInstalledCopy, string appId = AppId)
    {
        _isInstalledCopy = isInstalledCopy;
        _appId = appId;
    }

    public event EventHandler<NotificationActivation>? Activated;

    public bool IsAvailable
    {
        get
        {
            if (!_isInstalledCopy)
                return false;

            lock (_lock)
            {
                try
                {
                    // 0 means enabled; the other values say who turned notifications off.
                    return Notifier() is { } notifier && notifier.GetSetting() == 0;
                }
                catch (Exception ex) when (IsSystemRefusal(ex))
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Ties the windows of this process to the Start menu entry of Atlas, so that the taskbar and the notifications
    /// show them as one application. Only done when that entry exists, that is for an installed Atlas.
    /// </summary>
    public bool TryAdoptApplicationIdentity() =>
        IsAvailable && SetCurrentProcessExplicitAppUserModelID(_appId) == 0;

    public bool Show(DesktopNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!_isInstalledCopy)
            return false;

        lock (_lock)
        {
            try
            {
                var notifier = Notifier();
                if (notifier is null)
                    return false;

                var toast = Create(ToastXml.Build(notification));
                var tagged = (IToastNotification2)toast;
                using (var tag = new WindowsString(notification.Tag))
                using (var group = new WindowsString(Group))
                {
                    tagged.SetTag(tag.Handle);
                    tagged.SetGroup(group.Handle);
                }

                var handler = new ActivatedHandler(notification.Tag, OnActivated);
                toast.AddActivated(handler);

                if (_shown.Remove(notification.Tag, out var previous))
                    previous.Detach();

                notifier.Show(toast);
                _shown[notification.Tag] = new ShownNotification(toast, handler);
                return true;
            }
            catch (Exception ex) when (IsSystemRefusal(ex))
            {
                return false;
            }
        }
    }

    public void Remove(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        lock (_lock)
        {
            if (_shown.Remove(tag, out var shown))
                shown.Detach();

            try
            {
                using var tagText = new WindowsString(tag);
                using var groupText = new WindowsString(Group);
                using var appId = new WindowsString(_appId);
                History().RemoveGroupedTagWithId(tagText.Handle, groupText.Handle, appId.Handle);
            }
            catch (Exception ex) when (IsSystemRefusal(ex))
            {
                // Already gone, or the system cannot say: there is nothing more to remove.
            }
        }
    }

    public void RemoveAll()
    {
        lock (_lock)
        {
            foreach (var shown in _shown.Values)
                shown.Detach();
            _shown.Clear();

            try
            {
                using var appId = new WindowsString(_appId);
                History().ClearWithId(appId.Handle);
            }
            catch (Exception ex) when (IsSystemRefusal(ex))
            {
                // Same as above.
            }
        }
    }

    private void OnActivated(string tag, string argument) =>
        Activated?.Invoke(this, new NotificationActivation(tag, argument));

    private IToastNotifier? Notifier()
    {
        if (_notifier is not null)
            return _notifier;

        var statics = WinRt.Factory<IToastNotificationManagerStatics>(
            "Windows.UI.Notifications.ToastNotificationManager", typeof(IToastNotificationManagerStatics).GUID);
        using var appId = new WindowsString(_appId);
        _notifier = statics.CreateToastNotifierWithId(appId.Handle);
        return _notifier;
    }

    private static IToastNotificationHistory History()
    {
        var statics = WinRt.Factory<IToastNotificationManagerStatics2>(
            "Windows.UI.Notifications.ToastNotificationManager", typeof(IToastNotificationManagerStatics2).GUID);
        return statics.GetHistory();
    }

    private static IToastNotification Create(string xml)
    {
        var document = WinRt.Activate("Windows.Data.Xml.Dom.XmlDocument");
        try
        {
            var io = (IXmlDocumentIO)Marshal.GetObjectForIUnknown(document);
            using (var text = new WindowsString(xml))
                io.LoadXml(text.Handle);

            var content = WinRt.QueryInterface(document, typeof(IXmlDocument).GUID);
            try
            {
                var factory = WinRt.Factory<IToastNotificationFactory>(
                    "Windows.UI.Notifications.ToastNotification", typeof(IToastNotificationFactory).GUID);
                return factory.CreateToastNotification(content);
            }
            finally
            {
                Marshal.Release(content);
            }
        }
        finally
        {
            Marshal.Release(document);
        }
    }

    /// <summary>
    /// What the Windows Runtime throws when it cannot help: no identity registered for Atlas (element not found),
    /// notifications blocked by policy, or a system without notifications.
    /// </summary>
    private static bool IsSystemRefusal(Exception exception) =>
        exception is COMException or InvalidCastException or UnauthorizedAccessException or TypeLoadException
            or EntryPointNotFoundException or DllNotFoundException or PlatformNotSupportedException
        || exception.HResult == NotFound;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private sealed record ShownNotification(IToastNotification Toast, ActivatedHandler Handler)
    {
        public void Detach() => Handler.Detach();
    }

    /// <summary>Receives the choice of the user. Windows calls it on a thread of its own.</summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivatedHandler(string tag, Action<string, string> onActivated) : IToastActivatedHandler
    {
        private volatile bool _detached;

        public void Detach() => _detached = true;

        public void Invoke(nint sender, nint args)
        {
            if (_detached)
                return;

            var argument = string.Empty;
            if (args != nint.Zero)
            {
                var activated = WinRt.QueryInterface(args, typeof(IToastActivatedEventArgs).GUID);
                try
                {
                    var arguments = (IToastActivatedEventArgs)Marshal.GetObjectForIUnknown(activated);
                    argument = WindowsString.Read(arguments.GetArguments());
                }
                finally
                {
                    Marshal.Release(activated);
                }
            }

            onActivated(tag, argument);
        }
    }
}

/// <summary>Builds the XML Windows reads to draw a notification. Every text is escaped.</summary>
public static class ToastXml
{
    /// <summary>Windows shows at most five actions.</summary>
    public const int MaxActions = 5;

    public static string Build(DesktopNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var xml = new StringBuilder();
        xml.Append("<toast launch=\"\"><visual><binding template=\"ToastGeneric\">");
        xml.Append("<text>").Append(SecurityElement.Escape(notification.Title)).Append("</text>");
        xml.Append("<text>").Append(SecurityElement.Escape(notification.Message)).Append("</text>");
        xml.Append("</binding></visual>");

        if (notification.Actions.Count > 0)
        {
            xml.Append("<actions>");
            foreach (var action in notification.Actions.Take(MaxActions))
            {
                xml.Append("<action activationType=\"foreground\" content=\"")
                    .Append(SecurityElement.Escape(action.Label))
                    .Append("\" arguments=\"")
                    .Append(SecurityElement.Escape(action.Argument))
                    .Append("\" />");
            }

            xml.Append("</actions>");
        }

        return xml.Append("</toast>").ToString();
    }
}
