using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace NPVideoStudio.App.Services;

public sealed class StorageService : IStorageService
{
    private readonly Func<Window?> _getMainWindow;

    public StorageService(Func<Window?> getMainWindow)
    {
        _getMainWindow = getMainWindow;
    }

    private IStorageProvider? Provider => _getMainWindow()?.StorageProvider;

    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<(string Name, string[] Extensions)> filters, bool allowMultiple)
    {
        var provider = Provider;
        if (provider is null)
        {
            return Array.Empty<string>();
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Extensions.Select(e => $"*.{e}").ToArray() }).ToList()
        };

        var files = await provider.OpenFilePickerAsync(options);
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var provider = Provider;
        if (provider is null)
        {
            return null;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<(string Name, string[] Extensions)> filters)
    {
        var provider = Provider;
        if (provider is null)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Extensions.Select(e => $"*.{e}").ToArray() }).ToList()
        };

        var file = await provider.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
    }
}
