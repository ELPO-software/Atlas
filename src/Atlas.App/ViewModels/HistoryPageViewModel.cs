using Atlas.App.Resources;
using Atlas.App.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed record HistoryItemViewModel(string Text, string When, bool IsProblem);

public sealed partial class HistoryPageViewModel(PageServices services) : PageViewModel
{
    /// <summary>The page shows the most recent lines; older ones stay stored.</summary>
    internal const int ShownEntries = 500;

    private readonly ILogger _logger = services.Loggers.CreateLogger<HistoryPageViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<HistoryItemViewModel> Items { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    public bool IsEmpty => IsLoaded && Items.Count == 0;

    [RelayCommand(AllowConcurrentExecutions = true)]
    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var entries = await services.History.ListAsync(ShownEntries);
            Items = entries
                .Select(e => new HistoryItemViewModel(HistoryText.Describe(e), DateText.At(e.OccurredAt, services.Time), HistoryText.IsProblem(e)))
                .ToList();
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The history could not be read");
            LoadError = UiText.Format(Strings.HistoryReadFailed, services.Paths.LogsDirectory);
        }
    }
}
