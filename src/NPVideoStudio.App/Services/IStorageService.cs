namespace NPVideoStudio.App.Services;

/// <summary>Thin wrapper over Avalonia's IStorageProvider so ViewModels don't need a direct Window reference.</summary>
public interface IStorageService
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<(string Name, string[] Extensions)> filters, bool allowMultiple);

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<(string Name, string[] Extensions)> filters);
}
