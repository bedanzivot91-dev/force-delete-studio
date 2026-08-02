namespace NPVideoStudio.Core.Diagnostics;

/// <summary>Backs the "Alati i modeli" screen: real status for every external tool/model the app uses.</summary>
public interface IDependencyManagerService
{
    Task<IReadOnlyList<DependencyInfo>> GetDependenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the local Whisper speech-recognition model. Must only run after explicit user consent.</summary>
    Task DownloadWhisperModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Opens the OS file browser at the folder containing <paramref name="path"/>.</summary>
    void OpenContainingFolder(string path);
}
