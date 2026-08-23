using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsFilePath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService() : this(Path.Combine(AppSettings.AppDataRoot(), "settings.json"))
    {
    }

    /// <summary>Testability overload - lets tests point at an isolated file instead of the real per-user AppData settings file.</summary>
    public SettingsService(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_settingsFilePath))
            {
                Current = new AppSettings();
                AppSettings.ConfigureRuntimeCacheFolder(Current.CacheFolder);

                // First-run persistence is best effort. A locked-down/read-only profile must still be
                // able to open the application with defaults; an explicit settings save can report the
                // write problem later instead of turning startup into a fatal error.
                try
                {
                    await SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                return;
            }

            await using var stream = File.OpenRead(_settingsFilePath);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupted settings file must never crash the app on startup (spec §42/53) -
            // fall back to defaults and let the diagnostics screen surface the problem.
            Current = new AppSettings();
        }
        catch (IOException)
        {
            // Locked/unavailable settings are not a reason to make the whole application unstartable.
            Current = new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            // Same for a read-only or policy-restricted profile.
            Current = new AppSettings();
        }

        AppSettings.ConfigureRuntimeCacheFolder(Current.CacheFolder);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        AppSettings.ConfigureRuntimeCacheFolder(Current.CacheFolder);

        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
        var tempPath = Path.Combine(
            tempDirectory,
            $".{Path.GetFileName(_settingsFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             32 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(_settingsFilePath))
            {
                File.Replace(tempPath, _settingsFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _settingsFilePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Never mask the original settings-save error with cleanup noise.
            }
        }
    }

    public Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        Current = new AppSettings();
        AppSettings.ConfigureRuntimeCacheFolder(Current.CacheFolder);
        return SaveAsync(cancellationToken);
    }
}
