using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Versions;
using CommunityToolkit.Mvvm.Input;

namespace Atlas.App.ViewModels;

public sealed class VersionItemViewModel(VersionSummary version, TimeProvider time, Func<VersionSummary, Task> restore)
{
    public Guid Id => version.Id;

    public string Name => version.Name;

    public string SavedText { get; } = DateText.At(version.SavedAt, time);

    public bool IsAutomatic => version.IsAutomatic;

    public string ResourceCountText { get; } = UiText.ResourceCount(version.ResourceCount);

    public string RestoreLabel { get; } = UiText.Format(Strings.RestoreVersionLabel, version.Name);

    public IAsyncRelayCommand RestoreCommand { get; } =
        new AsyncRelayCommand(() => restore(version), AsyncRelayCommandOptions.AllowConcurrentExecutions);
}
