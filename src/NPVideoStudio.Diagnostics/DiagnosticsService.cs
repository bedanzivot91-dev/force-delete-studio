using System.IO.Compression;
using System.Runtime.InteropServices;
using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;

namespace NPVideoStudio.Diagnostics;

/// <summary>
/// Real, runnable system checks (spec §42). Every check either passes, warns, or explains in plain
/// language what is wrong, why it matters and how to fix it - never a bare "something went wrong".
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly ISettingsService _settingsService;
    private readonly AppDatabase _database;

    public DiagnosticsService(ISettingsService settingsService, AppDatabase database)
    {
        _settingsService = settingsService;
        _database = database;
    }

    public async Task<IReadOnlyList<DiagnosticCheckResult>> RunAllChecksAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosticCheckResult>
        {
            CheckDotNetRuntime(),
            await CheckToolAsync("FFmpeg", FfmpegLocator.ResolveFfmpegPath(_settingsService.Current.FfmpegPath), "-version", cancellationToken).ConfigureAwait(false),
            await CheckToolAsync("FFprobe", FfmpegLocator.ResolveFfprobePath(_settingsService.Current.FfprobePath), "-version", cancellationToken).ConfigureAwait(false),
            await CheckToolAsync("yt-dlp", FfmpegLocator.ResolveYtDlpPath(_settingsService.Current.YtDlpPath), "--version", cancellationToken).ConfigureAwait(false),
            CheckFolder("Folder za projekte", _settingsService.Current.ProjectsFolder, canAutoFix: true),
            CheckFolder("Cache folder", _settingsService.Current.CacheFolder, canAutoFix: true),
            CheckFolder("Folder za logove", AppSettings.LogsFolder(), canAutoFix: true),
            CheckDiskSpace(_settingsService.Current.ProjectsFolder),
            await CheckDatabaseAsync(cancellationToken).ConfigureAwait(false),
            CheckSettingsFile()
        };

        return results;
    }

    private static DiagnosticCheckResult CheckDotNetRuntime()
    {
        var version = RuntimeInformation.FrameworkDescription;
        var isDotNet8OrNewer = Environment.Version.Major >= 8;

        return new DiagnosticCheckResult
        {
            CheckName = ".NET Runtime",
            Status = isDotNet8OrNewer ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
            Description = isDotNet8OrNewer
                ? "Potrebna verzija .NET okruženja je instalirana."
                : "Instalirana je starija verzija .NET okruženja od potrebne (.NET 8).",
            WhyItMatters = "NP Video Studio zahteva .NET 8 da bi uopšte mogao da se pokrene.",
            HowToFix = isDotNet8OrNewer ? null : "Instalirajte .NET 8 Desktop Runtime sa zvaničnog Microsoft sajta ili pokrenite scripts/check-dependencies.ps1.",
            TechnicalDetails = version
        };
    }

    private static async Task<DiagnosticCheckResult> CheckToolAsync(
        string toolLabel, string executablePath, string versionArgument, CancellationToken cancellationToken)
    {
        var (found, version) = await FfmpegLocator.TryGetVersionAsync(executablePath, versionArgument, cancellationToken).ConfigureAwait(false);
        var isOptional = toolLabel == "yt-dlp";

        return new DiagnosticCheckResult
        {
            CheckName = toolLabel,
            Status = found ? DiagnosticStatus.Ok : isOptional ? DiagnosticStatus.Warning : DiagnosticStatus.Error,
            Description = found
                ? $"{toolLabel} je pronađen i radi ispravno."
                : $"{toolLabel} nije pronađen na sistemu.",
            WhyItMatters = isOptional
                ? "yt-dlp je potreban samo za alat „Preuzmi sa YouTube-a“ - ostatak programa radi i bez njega."
                : $"{toolLabel} je neophodan za uvoz, analizu i obradu video i audio fajlova.",
            HowToFix = found ? null : $"Pokrenite scripts/check-dependencies.ps1 da automatski preuzmete {toolLabel}, ili ga ručno instalirajte i dodajte u PATH, ili podesite putanju u Podešavanja → FFmpeg.",
            TechnicalDetails = found ? $"Putanja: {executablePath}\nVerzija: {version}" : $"Tražena putanja: {executablePath}"
        };
    }

    private static DiagnosticCheckResult CheckFolder(string label, string path, bool canAutoFix)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return new DiagnosticCheckResult
                {
                    CheckName = label,
                    Status = DiagnosticStatus.Warning,
                    Description = $"Folder ne postoji: {path}",
                    WhyItMatters = "Program ne može da sačuva fajlove na ovoj lokaciji dok folder ne bude napravljen.",
                    HowToFix = "Kliknite „Pokušaj automatsku popravku“ da folder bude napravljen automatski.",
                    TechnicalDetails = path,
                    CanAutoFix = canAutoFix
                };
            }

            var testFile = Path.Combine(path, $".np_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            return new DiagnosticCheckResult
            {
                CheckName = label,
                Status = DiagnosticStatus.Ok,
                Description = "Folder postoji i program može da upisuje u njega.",
                TechnicalDetails = path
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheckResult
            {
                CheckName = label,
                Status = DiagnosticStatus.Error,
                Description = $"Program nema dozvolu za upis u folder: {path}",
                WhyItMatters = "Bez dozvole za upis, projekti, keš i logovi ne mogu da se sačuvaju.",
                HowToFix = "Proverite dozvole foldera u Windows Explorer-u ili izaberite drugu lokaciju u Podešavanjima.",
                TechnicalDetails = ex.Message
            };
        }
    }

    private static DiagnosticCheckResult CheckDiskSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return new DiagnosticCheckResult
                {
                    CheckName = "Slobodan prostor na disku",
                    Status = DiagnosticStatus.Warning,
                    Description = "Nije moguće utvrditi disk na kome se nalazi folder za projekte."
                };
            }

            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            var status = freeGb < 2 ? DiagnosticStatus.Error : freeGb < 10 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;

            return new DiagnosticCheckResult
            {
                CheckName = "Slobodan prostor na disku",
                Status = status,
                Description = $"Slobodno je {freeGb:F1} GB na disku {root}.",
                WhyItMatters = status == DiagnosticStatus.Ok ? null : "Renderovanje i keš fajlovi zahtevaju dovoljno slobodnog prostora; mali prostor može prekinuti izvoz videa.",
                HowToFix = status == DiagnosticStatus.Ok ? null : "Oslobodite prostor na disku ili promenite folder za projekte/keš na disk sa više prostora.",
                TechnicalDetails = $"Ukupno: {drive.TotalSize / 1024.0 / 1024.0 / 1024.0:F1} GB, Slobodno: {freeGb:F1} GB"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheckResult
            {
                CheckName = "Slobodan prostor na disku",
                Status = DiagnosticStatus.Warning,
                Description = "Nije moguće proveriti slobodan prostor na disku.",
                TechnicalDetails = ex.Message
            };
        }
    }

    private async Task<DiagnosticCheckResult> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        var result = await _database.CheckIntegrityAsync(cancellationToken).ConfigureAwait(false);
        var ok = string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);

        return new DiagnosticCheckResult
        {
            CheckName = "Lokalna baza podataka",
            Status = ok ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
            Description = ok ? "Baza podataka je ispravna." : "Baza podataka je oštećena.",
            WhyItMatters = ok ? null : "Lista nedavnih projekata i lokalna podešavanja mogu biti nedostupni ili netačni.",
            HowToFix = ok ? null : $"Zatvorite program i obrišite fajl {AppSettings.DatabasePath()} - baza će biti automatski ponovo napravljena, ali lista nedavnih projekata će biti izgubljena (sami projekti ostaju sačuvani na disku).",
            TechnicalDetails = result
        };
    }

    private DiagnosticCheckResult CheckSettingsFile()
    {
        var path = Path.Combine(AppSettings.AppDataRoot(), "settings.json");
        if (!File.Exists(path))
        {
            return new DiagnosticCheckResult
            {
                CheckName = "Fajl podešavanja",
                Status = DiagnosticStatus.Ok,
                Description = "Podešavanja još nisu sačuvana; koriste se podrazumevane vrednosti.",
                TechnicalDetails = path
            };
        }

        try
        {
            var content = File.ReadAllText(path);
            System.Text.Json.JsonDocument.Parse(content);
            return new DiagnosticCheckResult
            {
                CheckName = "Fajl podešavanja",
                Status = DiagnosticStatus.Ok,
                Description = "Fajl podešavanja je ispravan.",
                TechnicalDetails = path
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheckResult
            {
                CheckName = "Fajl podešavanja",
                Status = DiagnosticStatus.Error,
                Description = "Fajl podešavanja je oštećen i ne može da se učita.",
                WhyItMatters = "Program će koristiti podrazumevana podešavanja dok se ovo ne reši.",
                HowToFix = "Kliknite „Pokušaj automatsku popravku“ da se podešavanja vrate na podrazumevane vrednosti.",
                TechnicalDetails = ex.Message,
                CanAutoFix = true
            };
        }
    }

    public Task<DiagnosticCheckResult> TryAutoFixAsync(string checkName, CancellationToken cancellationToken = default)
    {
        switch (checkName)
        {
            case "Folder za projekte":
                Directory.CreateDirectory(_settingsService.Current.ProjectsFolder);
                return Task.FromResult(CheckFolder(checkName, _settingsService.Current.ProjectsFolder, canAutoFix: true));

            case "Cache folder":
                Directory.CreateDirectory(_settingsService.Current.CacheFolder);
                return Task.FromResult(CheckFolder(checkName, _settingsService.Current.CacheFolder, canAutoFix: true));

            case "Folder za logove":
                Directory.CreateDirectory(AppSettings.LogsFolder());
                return Task.FromResult(CheckFolder(checkName, AppSettings.LogsFolder(), canAutoFix: true));

            case "Fajl podešavanja":
                return FixSettingsFileAsync(cancellationToken);

            default:
                return Task.FromResult(new DiagnosticCheckResult
                {
                    CheckName = checkName,
                    Status = DiagnosticStatus.Warning,
                    Description = "Za ovu proveru ne postoji automatska popravka.",
                    HowToFix = "Pratite uputstvo u opisu problema."
                });
        }
    }

    private async Task<DiagnosticCheckResult> FixSettingsFileAsync(CancellationToken cancellationToken)
    {
        await _settingsService.ResetToDefaultsAsync(cancellationToken).ConfigureAwait(false);
        return CheckSettingsFile();
    }

    public async Task<string> CreateSupportPackageAsync(string destinationFolder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationFolder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(destinationFolder, $"NPVideoStudio-Support-{timestamp}.zip");

        var stagingDir = Path.Combine(Path.GetTempPath(), $"npvs_support_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            var logsFolder = AppSettings.LogsFolder();
            if (Directory.Exists(logsFolder))
            {
                var logsDest = Path.Combine(stagingDir, "Logs");
                Directory.CreateDirectory(logsDest);
                foreach (var file in Directory.GetFiles(logsFolder))
                {
                    File.Copy(file, Path.Combine(logsDest, Path.GetFileName(file)), overwrite: true);
                }
            }

            var checks = await RunAllChecksAsync(cancellationToken).ConfigureAwait(false);
            var systemInfoLines = new List<string>
            {
                "NP Video Studio - Paket za podršku",
                $"Napravljen: {DateTimeOffset.Now:O}",
                $"OS: {RuntimeInformation.OSDescription}",
                $"Arhitektura: {RuntimeInformation.OSArchitecture}",
                $".NET: {RuntimeInformation.FrameworkDescription}",
                string.Empty,
                "Rezultati dijagnostike:"
            };
            foreach (var check in checks)
            {
                systemInfoLines.Add($"- [{check.Status}] {check.CheckName}: {check.Description}");
                if (check.TechnicalDetails is not null)
                {
                    systemInfoLines.Add($"    {check.TechnicalDetails.Replace("\n", "\n    ")}");
                }
            }

            await File.WriteAllLinesAsync(Path.Combine(stagingDir, "system-info.txt"), systemInfoLines, cancellationToken).ConfigureAwait(false);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
            ZipFile.CreateFromDirectory(stagingDir, zipPath);

            return zipPath;
        }
        finally
        {
            Directory.Delete(stagingDir, recursive: true);
        }
    }
}
