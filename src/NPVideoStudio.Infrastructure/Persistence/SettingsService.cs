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
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_settingsFilePath))
        {
            Current = new AppSettings();
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
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
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var tempPath = _settingsFilePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(_settingsFilePath))
        {
            File.Replace(tempPath, _settingsFilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, _settingsFilePath);
        }
    }

    public Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        Current = new AppSettings();
        return SaveAsync(cancellationToken);
    }
}
