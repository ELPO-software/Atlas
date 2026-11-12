using System.Security.Cryptography;
using System.Text;
using Atlas.App.Text;
using Atlas.App.Views;
using Atlas.Core.Detection;
using Atlas.Core.Notifications;
using Atlas.Core.Settings;
using Atlas.Core.Workspaces;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Services;

/// <summary>
/// Runs the detection while Atlas is open and brings its proposals to the user without getting in the way: at most one
/// announcement at a time, as a Windows notification when Atlas is in the background, or as a banner inside Atlas.
/// Nothing is observed before the user agrees, and nothing is saved without the user asking for it.
/// </summary>
public sealed partial class SuggestionCenter : ObservableObject, IDisposable
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan FirstScanDelay = TimeSpan.FromSeconds(20);

    internal const string SaveArgument = "save";
    internal const string ReviewArgument = "review";
    internal const string LaterArgument = "later";
    internal const string IgnoreArgument = "ignore";

    private readonly EnvironmentDetectionService _detection;
    private readonly SettingsService _settings;
    private readonly IDesktopNotifications _notifications;
    private readonly MainWindowHost _host;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<string, string> _keysByTag = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loop;
    private bool _isScanning;

    public SuggestionCenter(
        EnvironmentDetectionService detection,
        SettingsService settings,
        IDesktopNotifications notifications,
        MainWindowHost host,
        TimeProvider time,
        ILoggerFactory loggers)
    {
        _detection = detection;
        _settings = settings;
        _notifications = notifications;
        _host = host;
        _time = time;
        _logger = loggers.CreateLogger<SuggestionCenter>();
        _notifications.Activated += OnNotificationActivated;
    }

    /// <summary>Raised when the user asks to see the suggestions, with the key of the one to show first, if any.</summary>
    public event EventHandler<string?>? SuggestionsRequested;

    /// <summary>Raised when a suggestion became a workspace from a notification, to show it.</summary>
    public event EventHandler<Guid>? WorkspaceSaved;

    /// <summary>Whether the user allowed the detection. Null until the user decides.</summary>
    [ObservableProperty]
    public partial bool? IsEnabled { get; private set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Count), nameof(HasSuggestions))]
    public partial IReadOnlyList<StoredProposal> Suggestions { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastScanAt { get; private set; }

    [ObservableProperty]
    public partial bool ScanFailed { get; private set; }

    /// <summary>The proposal announced by the banner of the main window, if any.</summary>
    [ObservableProperty]
    public partial StoredProposal? Announcement { get; private set; }

    public int Count => Suggestions.Count;

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>False for a copy of Atlas that Windows cannot attribute notifications to, such as the portable one.</summary>
    public bool DesktopNotificationsAvailable => _notifications.IsAvailable;

    public async Task StartAsync()
    {
        try
        {
            IsEnabled = await _settings.GetDetectionEnabledAsync();
            NotificationsEnabled = await _settings.GetDetectionNotificationsAsync();
            if (IsEnabled == true)
            {
                Suggestions = await _detection.ListSuggestionsAsync();
                StartLoop(FirstScanDelay);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Atlas works without detection; the suggestions page reports the problem.
            _logger.LogError(ex, "The detection could not start");
            ScanFailed = true;
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await _settings.SetDetectionEnabledAsync(enabled);
        IsEnabled = enabled;

        if (enabled)
        {
            StartLoop(TimeSpan.Zero);
            return;
        }

        StopLoop();
        Announcement = null;
        Suggestions = [];
        _notifications.RemoveAll();
    }

    public async Task SetNotificationsEnabledAsync(bool enabled)
    {
        await _settings.SetDetectionNotificationsAsync(enabled);
        NotificationsEnabled = enabled;
        if (!enabled)
            _notifications.RemoveAll();
    }

    /// <summary>Observes the session now, at the user's request. Its result is shown, never announced.</summary>
    public Task ScanNowAsync() => ScanAsync(announce: false, CancellationToken.None);

    public async Task<Workspace> SaveAsync(string key, string? name, IReadOnlyList<ProposedResource> resources)
    {
        var workspace = await _detection.SaveAsync(key, name, resources);
        await AfterDecisionAsync(key);
        return workspace;
    }

    public async Task SnoozeAsync(string key)
    {
        await _detection.SnoozeAsync(key);
        await AfterDecisionAsync(key);
    }

    public async Task IgnoreAsync(string key)
    {
        await _detection.IgnoreAsync(key);
        await AfterDecisionAsync(key);
    }

    public async Task RestoreIgnoredAsync(string key)
    {
        await _detection.RestoreIgnoredAsync(key);
        Suggestions = await _detection.ListSuggestionsAsync();
    }

    public Task<IReadOnlyList<StoredProposal>> ListIgnoredAsync() => _detection.ListIgnoredAsync();

    /// <summary>Forgets what Atlas observed. Workspaces saved from suggestions stay.</summary>
    public async Task ForgetObservationsAsync()
    {
        await _detection.ForgetObservationsAsync();
        Announcement = null;
        Suggestions = [];
        _notifications.RemoveAll();
    }

    public void DismissAnnouncement() => Announcement = null;

    public void Dispose()
    {
        StopLoop();
        _notifications.Activated -= OnNotificationActivated;

        // Nothing would answer a notification left behind once Atlas is closed.
        _notifications.RemoveAll();
    }

    /// <summary>What the user chose in a Windows notification. Called on the interface thread.</summary>
    internal async Task HandleActivationAsync(NotificationActivation activation)
    {
        _notifications.Remove(activation.Tag);
        _keysByTag.TryGetValue(activation.Tag, out var key);
        var proposal = key is null ? null : (await _detection.ListSuggestionsAsync()).FirstOrDefault(s => s.Key == key);

        try
        {
            switch (activation.Argument)
            {
                // Only a certain environment can be saved from the notification; a probable one is reviewed first.
                case SaveArgument when proposal is { Proposal.Confidence: DetectionConfidence.Detected }:
                    var workspace = await SaveAsync(proposal.Key, proposal.Proposal.Title, proposal.Proposal.Resources);
                    WindowActivation.BringToFront(_host.Window);
                    WorkspaceSaved?.Invoke(this, workspace.Id);
                    return;

                case LaterArgument when proposal is not null:
                    await SnoozeAsync(proposal.Key);
                    return;

                case IgnoreArgument when proposal is not null:
                    await IgnoreAsync(proposal.Key);
                    return;

                default:
                    WindowActivation.BringToFront(_host.Window);
                    SuggestionsRequested?.Invoke(this, proposal?.Key);
                    return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The suggestion stays; the page shows it again so the user can decide there.
            _logger.LogError(ex, "A choice made in a notification could not be applied");
            WindowActivation.BringToFront(_host.Window);
            SuggestionsRequested?.Invoke(this, proposal?.Key);
        }
    }

    /// <summary>A short tag Windows can store, the same for the same environment.</summary>
    internal static string TagOf(string key) =>
        "atlas-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8).ToLowerInvariant();

    private async Task AfterDecisionAsync(string key)
    {
        if (Announcement?.Key == key)
            Announcement = null;

        _notifications.Remove(TagOf(key));
        Suggestions = await _detection.ListSuggestionsAsync();
    }

    private void StartLoop(TimeSpan firstDelay)
    {
        StopLoop();
        _loop = new CancellationTokenSource();
        _ = RunLoopAsync(firstDelay, _loop.Token);
    }

    private void StopLoop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
    }

    private async Task RunLoopAsync(TimeSpan firstDelay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(firstDelay, _time, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                await ScanAsync(announce: true, cancellationToken);
                await Task.Delay(ScanInterval, _time, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The detection was turned off, or Atlas is closing.
        }
    }

    private async Task ScanAsync(bool announce, CancellationToken cancellationToken)
    {
        if (_isScanning || IsEnabled != true)
            return;

        _isScanning = true;
        IsScanning = true;
        try
        {
            var scan = await _detection.ScanAsync(cancellationToken);
            Suggestions = scan.Suggestions;
            LastScanAt = scan.At;
            ScanFailed = false;

            // A suggestion decided elsewhere no longer needs its banner.
            if (Announcement is { } shown && scan.Suggestions.All(s => s.Key != shown.Key))
                Announcement = null;

            if (announce && scan.ToNotify is { } proposal)
                await AnnounceAsync(proposal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The session could not be observed");
            ScanFailed = true;
        }
        finally
        {
            _isScanning = false;
            IsScanning = false;
        }
    }

    private async Task AnnounceAsync(StoredProposal proposal)
    {
        var shownOnDesktop = false;

        // Inside Atlas, a banner is enough; a Windows notification is for when Atlas is in the background.
        if (_host.Window?.IsActive != true && NotificationsEnabled && _notifications.IsAvailable)
        {
            var tag = TagOf(proposal.Key);
            _keysByTag[tag] = proposal.Key;
            shownOnDesktop = _notifications.Show(SuggestionText.Notification(proposal, tag));
        }

        if (!shownOnDesktop)
            Announcement = proposal;

        await _detection.MarkNotifiedAsync(proposal.Key);
    }

    private void OnNotificationActivated(object? sender, NotificationActivation activation) =>
        Dispatcher.UIThread.Post(() => _ = HandleActivationAsync(activation));
}
