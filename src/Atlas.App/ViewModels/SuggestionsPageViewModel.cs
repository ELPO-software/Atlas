using System.Collections.ObjectModel;
using System.ComponentModel;
using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Detection;
using Atlas.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

/// <summary>The environments Atlas proposes, and the decisions the user takes about them.</summary>
public sealed partial class SuggestionsPageViewModel : PageViewModel, IDisposable
{
    private readonly PageServices _services;
    private readonly INavigator _navigator;
    private readonly ILogger _logger;
    private readonly string? _focusKey;

    public SuggestionsPageViewModel(PageServices services, INavigator navigator, string? focusKey = null)
    {
        _services = services;
        _navigator = navigator;
        _focusKey = focusKey;
        _logger = services.Loggers.CreateLogger<SuggestionsPageViewModel>();
        Center.PropertyChanged += OnCenterChanged;
    }

    public SuggestionCenter Center => _services.Suggestions;

    public ObservableCollection<SuggestionCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<IgnoredSuggestionViewModel> Ignored { get; private set; } = [];

    [ObservableProperty]
    public partial string? ActionError { get; private set; }

    public bool IsUndecided => Center.IsEnabled is null;

    public bool IsDisabled => Center.IsEnabled == false;

    public bool IsEnabled => Center.IsEnabled == true;

    public bool IsEmpty => IsEnabled && Cards.Count == 0 && !Center.IsScanning;

    public bool HasIgnored => Ignored.Count > 0;

    public string ScanFailedText => UiText.Format(Strings.ScanFailedMessage, _services.Paths.LogsDirectory);

    public string IgnoredTitle => UiText.Format(Strings.IgnoredSuggestionsTitle, Ignored.Count);

    public string LastScanText =>
        Center.IsScanning ? Strings.ScanInProgress
        : Center.LastScanAt is { } at ? UiText.Format(Strings.LastScan, DateText.When(at, _services.Time))
        : Strings.ScanNotYet;

    public override async Task LoadAsync()
    {
        Refresh();
        await LoadIgnoredAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task EnableAsync() => await RunAsync(() => Center.SetEnabledAsync(true));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DeclineAsync() => await RunAsync(() => Center.SetEnabledAsync(false));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ScanAsync() => await RunAsync(Center.ScanNowAsync);

    public void Dispose() => Center.PropertyChanged -= OnCenterChanged;

    internal async Task SaveAsync(SuggestionCardViewModel card)
    {
        card.Error = null;
        if (card.SelectedResources.Count == 0)
        {
            card.Error = Strings.SuggestionNothingSelected;
            return;
        }

        try
        {
            var workspace = await Center.SaveAsync(card.Key, card.Name, card.SelectedResources);
            await _navigator.OpenWorkspaceAsync(workspace.Id);
        }
        catch (AtlasValidationException ex)
        {
            card.Error = ValidationText.Describe(ex.Errors[0]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "A suggestion could not be saved");
            card.Error = UiText.Format(Strings.SaveFailed, _services.Paths.LogsDirectory);
        }
    }

    internal Task SnoozeAsync(SuggestionCardViewModel card) => RunAsync(() => Center.SnoozeAsync(card.Key), card);

    internal Task IgnoreAsync(SuggestionCardViewModel card) => RunAsync(() => Center.IgnoreAsync(card.Key), card);

    internal async Task RestoreAsync(IgnoredSuggestionViewModel item)
    {
        await RunAsync(() => Center.RestoreIgnoredAsync(item.Key));
        await LoadIgnoredAsync();
    }

    private async Task RunAsync(Func<Task> action, SuggestionCardViewModel? card = null)
    {
        ActionError = null;
        try
        {
            await action();
            await LoadIgnoredAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "A choice about suggestions could not be applied");
            var message = UiText.Format(Strings.SaveFailed, _services.Paths.LogsDirectory);
            if (card is null)
                ActionError = message;
            else
                card.Error = message;
        }
    }

    private async Task LoadIgnoredAsync()
    {
        try
        {
            Ignored = (await Center.ListIgnoredAsync()).Select(p => new IgnoredSuggestionViewModel(p, this)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The ignored suggestions could not be read");
            Ignored = [];
        }

        OnPropertyChanged(nameof(HasIgnored));
        OnPropertyChanged(nameof(IgnoredTitle));
    }

    /// <summary>Keeps the cards in step with the suggestions, without losing what the user is editing.</summary>
    private void Refresh()
    {
        var suggestions = Center.Suggestions;
        var keys = suggestions.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        for (var i = Cards.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(Cards[i].Key))
                Cards.RemoveAt(i);
        }

        foreach (var suggestion in suggestions)
        {
            var card = Cards.FirstOrDefault(c => c.Key == suggestion.Key);
            if (card is null)
            {
                card = new SuggestionCardViewModel(suggestion, this, _services.Time);
                if (suggestion.Key == _focusKey)
                    Cards.Insert(0, card);
                else
                    Cards.Add(card);
            }
            else
            {
                card.Update(suggestion);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnCenterChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SuggestionCenter.Suggestions):
                Refresh();
                break;
            case nameof(SuggestionCenter.IsEnabled):
                OnPropertyChanged(nameof(IsUndecided));
                OnPropertyChanged(nameof(IsDisabled));
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(IsEmpty));
                break;
            case nameof(SuggestionCenter.IsScanning):
            case nameof(SuggestionCenter.LastScanAt):
                OnPropertyChanged(nameof(LastScanText));
                OnPropertyChanged(nameof(IsEmpty));
                break;
        }
    }
}

/// <summary>One proposed environment: its name, the resources the user keeps, and the three decisions.</summary>
public sealed partial class SuggestionCardViewModel : ViewModelBase
{
    private readonly SuggestionsPageViewModel _page;
    private readonly TimeProvider _time;

    public SuggestionCardViewModel(StoredProposal suggestion, SuggestionsPageViewModel page, TimeProvider time)
    {
        _page = page;
        _time = time;
        Key = suggestion.Key;
        Name = SuggestionText.Title(suggestion);
        IconKind = suggestion.Proposal.Kind == ProposalKind.Project ? "folder" : "application";
        Update(suggestion);
    }

    public string Key { get; }

    /// <summary>The resource type whose icon stands for the environment: a folder for a project.</summary>
    public string IconKind { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string KindText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfidenceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsProbable { get; private set; }

    [ObservableProperty]
    public partial string Explanation { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? SnoozedText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ResourceChoiceViewModel> Resources { get; private set; } = [];

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string SaveLabel => UiText.Format(Strings.SuggestionSaveLabel, Title);

    public IReadOnlyList<ProposedResource> SelectedResources => Resources.Where(r => r.IsSelected).Select(r => r.Resource).ToList();

    internal void Update(StoredProposal suggestion)
    {
        var selection = Resources.ToDictionary(r => EnvironmentDetector.Identity(r.Resource), r => r.IsSelected, StringComparer.Ordinal);

        Title = SuggestionText.Title(suggestion);
        KindText = SuggestionText.Kind(suggestion.Proposal.Kind);
        ConfidenceText = SuggestionText.Confidence(suggestion.Proposal.Confidence);
        IsProbable = suggestion.Proposal.Confidence == DetectionConfidence.Probable;
        Explanation = SuggestionText.Explanation(suggestion);
        SnoozedText = suggestion.Status == ProposalStatus.Snoozed && suggestion.SnoozedUntil is { } until
            ? UiText.Format(Strings.SuggestionSnoozedUntil, DateText.When(until, _time))
            : null;

        // What the user unticked stays unticked when the environment is observed again.
        Resources = suggestion.Proposal.Resources
            .Select(r => new ResourceChoiceViewModel(r, selection.GetValueOrDefault(EnvironmentDetector.Identity(r), true)))
            .ToList();
        OnPropertyChanged(nameof(SaveLabel));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SaveAsync() => _page.SaveAsync(this);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LaterAsync() => _page.SnoozeAsync(this);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task IgnoreAsync() => _page.IgnoreAsync(this);
}

public sealed partial class ResourceChoiceViewModel(ProposedResource resource, bool isSelected) : ViewModelBase
{
    public ProposedResource Resource { get; } = resource;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;

    public string Name => Resource.DisplayName;

    public string Target => Resource.Target;

    /// <summary>The type of the resource, which picks its icon.</summary>
    public string Kind => Resource.Kind.Value;

    public string ReasonText => SuggestionText.Reason(Resource.Reason);

    public string ChoiceLabel => UiText.Format(Strings.KeepResourceLabel, Resource.DisplayName);
}

public sealed partial class IgnoredSuggestionViewModel(StoredProposal proposal, SuggestionsPageViewModel page) : ViewModelBase
{
    public string Key => proposal.Key;

    public string Title { get; } = SuggestionText.Title(proposal);

    public string RestoreLabel { get; } = UiText.Format(Strings.RestoreSuggestionLabel, SuggestionText.Title(proposal));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RestoreAsync() => page.RestoreAsync(this);
}
