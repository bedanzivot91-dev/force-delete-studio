using System.Security.Cryptography;
using System.Text;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

/// <summary>
/// Periodically saves the currently open project to a per-project autosave slot, separate from the
/// user's own .npvsproject file, so an unexpected shutdown never loses work (spec §3/33/53).
/// </summary>
public sealed class AutoSaveService : IAutoSaveService, IDisposable
{
    private readonly IProjectRepository _projectRepository;
    private readonly ISettingsService _settingsService;
    private Timer? _timer;
    private Func<Project?>? _getCurrentProject;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly string CleanShutdownMarkerPath =
        Path.Combine(AppSettings.AppDataRoot(), "clean_shutdown.marker");

    public AutoSaveService(IProjectRepository projectRepository, ISettingsService settingsService)
    {
        _projectRepository = projectRepository;
        _settingsService = settingsService;
    }

    public void Start(Func<Project?> getCurrentProject)
    {
        _getCurrentProject = getCurrentProject;

        // A missing marker means the previous session never called MarkCleanShutdownAsync, i.e. it crashed.
        //
        // Deleting it must never be able to take the whole startup down with it, which is exactly what the
        // unguarded File.Delete here used to do: the marker lives at one fixed per-user path, so two app
        // instances starting at the same moment race for it and the loser gets
        // "the process cannot access the file ... because it is being used by another process" thrown
        // straight out of the MainWindowViewModel constructor - the app fails to open at all. Found for
        // real on CI, where several tests construct the view model concurrently, but the same race is
        // reachable on a user's machine simply by launching the app twice quickly.
        //
        // Losing this delete is harmless either way: the marker is only a hint about whether the last
        // session ended cleanly, and the worst case is one unnecessary "recover your work?" prompt.
        try
        {
            if (File.Exists(CleanShutdownMarkerPath))
            {
                File.Delete(CleanShutdownMarkerPath);
            }
        }
        catch (IOException)
        {
            // Another instance holds it right now - it will clean up after itself.
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only/locked down profile - not worth failing startup over.
        }

        var settings = _settingsService.Current;
        if (!settings.AutoSaveEnabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, settings.AutoSaveIntervalSeconds));
        _timer = new Timer(OnTimerElapsed, null, interval, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimerElapsed(object? state)
    {
        _ = TriggerNowAsync();
    }

    public async Task TriggerNowAsync(CancellationToken cancellationToken = default)
    {
        var project = _getCurrentProject?.Invoke();
        if (project is null || string.IsNullOrEmpty(project.ProjectFilePath))
        {
            return;
        }

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var autoSavePath = AutoSavePathFor(project.ProjectFilePath);
            Directory.CreateDirectory(AppSettings.AutoSaveFolder());
            await _projectRepository.SaveAsync(project, autoSavePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public Task<string?> FindRecoverableAutoSaveAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var autoSavePath = AutoSavePathFor(projectFilePath);
        if (!File.Exists(autoSavePath))
        {
            return Task.FromResult<string?>(null);
        }

        var projectExists = File.Exists(projectFilePath);
        if (!projectExists)
        {
            return Task.FromResult<string?>(autoSavePath);
        }

        var autoSaveTime = File.GetLastWriteTimeUtc(autoSavePath);
        var projectTime = File.GetLastWriteTimeUtc(projectFilePath);

        return Task.FromResult(autoSaveTime > projectTime ? autoSavePath : null);
    }

    public Task MarkCleanShutdownAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppSettings.AppDataRoot());
        return File.WriteAllTextAsync(CleanShutdownMarkerPath, DateTimeOffset.Now.ToString("O"), cancellationToken);
    }

    private static string AutoSavePathFor(string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return Path.Combine(AppSettings.AutoSaveFolder(), $"{hash}.npvsproject");
    }

    public void Dispose() => Stop();
}
