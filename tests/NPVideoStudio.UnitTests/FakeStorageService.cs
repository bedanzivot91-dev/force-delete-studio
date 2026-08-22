using NPVideoStudio.App.Services;

namespace NPVideoStudio.UnitTests;

/// <summary>Fake so ViewModel command tests can drive file/folder pickers without a real Window.</summary>
public sealed class FakeStorageService : IStorageService
{
    public IReadOnlyList<string> FilesToReturn { get; set; } = Array.Empty<string>();
    public string? FolderToReturn { get; set; }
    public string? SaveFileToReturn { get; set; }

    public Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<(string Name, string[] Extensions)> filters, bool allowMultiple) =>
        Task.FromResult(FilesToReturn);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderToReturn);

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<(string Name, string[] Extensions)> filters) =>
        Task.FromResult(SaveFileToReturn);
}
