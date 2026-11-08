using Atlas.App.Resources;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Atlas.App.Services;

/// <summary>Lets the user point at a program, a file or a folder with the system's own dialog.</summary>
public interface IFilePicker
{
    /// <returns>The chosen path, or null if the user cancelled.</returns>
    Task<string?> PickProgramAsync();

    Task<string?> PickFileAsync();

    Task<string?> PickFolderAsync();
}

internal sealed class StorageFilePicker(Func<TopLevel?> owner) : IFilePicker
{
    public Task<string?> PickProgramAsync() =>
        PickAsync(Strings.PickProgramTitle, [new FilePickerFileType(Strings.ProgramsFilter) { Patterns = ["*.exe"] }]);

    public Task<string?> PickFileAsync() => PickAsync(Strings.PickFileTitle, filters: null);

    public async Task<string?> PickFolderAsync()
    {
        if (owner()?.StorageProvider is not { CanPickFolder: true } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.PickFolderTitle,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickAsync(string title, IReadOnlyList<FilePickerFileType>? filters)
    {
        if (owner()?.StorageProvider is not { CanOpen: true } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
