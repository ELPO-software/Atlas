namespace Atlas.Core.Notifications;

/// <param name="Tag">Identifies the notification, to replace or remove it later.</param>
public sealed record DesktopNotification(string Tag, string Title, string Message, IReadOnlyList<NotificationAction> Actions);

/// <param name="Argument">What <see cref="IDesktopNotifications.Activated"/> reports when the user chooses this action.</param>
public sealed record NotificationAction(string Label, string Argument);

/// <param name="Argument">The argument of the chosen action, or empty when the user clicked the notification itself.</param>
public sealed record NotificationActivation(string Tag, string Argument);

/// <summary>The notifications of the operating system, for the moments when Atlas is not in front of the user.</summary>
public interface IDesktopNotifications
{
    /// <summary>
    /// False when the system cannot show notifications for Atlas: on other systems, for a copy of Atlas that was not
    /// installed, or when the user turned them off. Atlas then shows its proposals in its own window only.
    /// </summary>
    bool IsAvailable { get; }

    /// <returns>False if the system did not accept the notification.</returns>
    bool Show(DesktopNotification notification);

    void Remove(string tag);

    /// <summary>Removes every notification of Atlas, so that none is left behind once Atlas cannot answer it.</summary>
    void RemoveAll();

    /// <summary>Raised on any thread when the user chooses a notification or one of its actions.</summary>
    event EventHandler<NotificationActivation>? Activated;
}

/// <summary>For systems, or copies of Atlas, that cannot show notifications.</summary>
public sealed class NoDesktopNotifications : IDesktopNotifications
{
    public bool IsAvailable => false;

    public bool Show(DesktopNotification notification) => false;

    public void Remove(string tag)
    {
    }

    public void RemoveAll()
    {
    }

    public event EventHandler<NotificationActivation>? Activated
    {
        add { }
        remove { }
    }
}
