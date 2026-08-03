using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed record CodecOption(VideoCodec Value, string Label);

/// <summary>
/// Export/render screen (spec Phase 9): configures <see cref="RenderSettings"/> and runs any number of
/// queued <see cref="RenderJob"/>s concurrently against the open project's timeline via
/// <see cref="IRenderService"/>. Real scope for this pass (see PHASE_STATUS.md): jobs all render the same
/// project snapshot passed at construction time - re-opening this screen after further timeline edits
/// picks up the latest state since <see cref="Project"/> is the same live object the workspace edits.
/// </summary>
public sealed partial class RenderQueueViewModel : ViewModelBase, IDisposable
{
    private readonly Project _project;
    private readonly IRenderService _renderService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private bool _outputPathConfirmedForOverwrite;

    public ObservableCollection<RenderJobItemViewModel> Jobs { get; } = new();

    public IReadOnlyList<CodecOption> CodecOptions { get; } = new[]
    {
        new CodecOption(VideoCodec.Libx264, "H.264 (softver - libx264, najpouzdaniji)"),
        new CodecOption(VideoCodec.H264Nvenc, "H.264 NVENC (Nvidia GPU)"),
        new CodecOption(VideoCodec.H264Qsv, "H.264 QSV (Intel GPU)"),
        new CodecOption(VideoCodec.H264Amf, "H.264 AMF (AMD GPU)")
    };

    public IReadOnlyList<string> PresetOptions { get; } = new[]
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
    };

    [ObservableProperty]
    private CodecOption _selectedCodecOption;

    [ObservableProperty]
    private string _preset = "medium";

    [ObservableProperty]
    private int _crf = 18;

    [ObservableProperty]
    private int _audioBitrateKbps = 192;

    [ObservableProperty]
    private string? _outputFilePath;

    partial void OnOutputFilePathChanged(string? value) => _outputPathConfirmedForOverwrite = false;

    [ObservableProperty]
    private string? _statusMessage;

    public event Action? BackRequested;

    public RenderQueueViewModel(Project project, IRenderService renderService, IStorageService storageService, ILogger logger)
    {
        _project = project;
        _renderService = renderService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(RenderQueueViewModel));
        _selectedCodecOption = CodecOptions[0];

        var directory = string.IsNullOrEmpty(project.ProjectFilePath) ? null : Path.GetDirectoryName(project.ProjectFilePath);
        var defaultName = $"{SanitizeFileName(project.Name)}_captioned.mp4";
        OutputFilePath = string.IsNullOrEmpty(directory) ? defaultName : Path.Combine(directory, defaultName);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => RefreshJobs();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    private void RefreshJobs()
    {
        foreach (var item in Jobs)
        {
            item.RefreshFromJob();
        }
    }

    [RelayCommand]
    private async Task PickOutputFileAsync()
    {
        var suggested = string.IsNullOrEmpty(OutputFilePath) ? "izvoz.mp4" : Path.GetFileName(OutputFilePath);
        var path = await _storageService.PickSaveFileAsync(
            "Izaberi izlazni fajl za izvoz", suggested, new[] { ("MP4 video", new[] { "mp4" }) });

        if (path is null)
        {
            return;
        }

        OutputFilePath = path;
        // The native save dialog already asked the user to confirm overwrite if this path exists.
        _outputPathConfirmedForOverwrite = true;
    }

    [RelayCommand]
    private void StartRender()
    {
        if (string.IsNullOrWhiteSpace(OutputFilePath))
        {
            StatusMessage = "Unesite ili izaberite izlaznu putanju pre pokretanja izvoza.";
            return;
        }

        if (File.Exists(OutputFilePath) && !_outputPathConfirmedForOverwrite)
        {
            StatusMessage = "Fajl na toj putanji već postoji - koristite dugme „Izaberi fajl“ da potvrdite prepisivanje, ili unesite drugo ime.";
            return;
        }

        var overwriteConfirmed = _outputPathConfirmedForOverwrite || !File.Exists(OutputFilePath);
        _outputPathConfirmedForOverwrite = false;

        var job = new RenderJob
        {
            ProjectName = _project.Name,
            Settings = new RenderSettings
            {
                OutputFilePath = OutputFilePath,
                Codec = SelectedCodecOption.Value,
                Crf = Crf,
                Preset = Preset,
                AudioBitrateKbps = AudioBitrateKbps,
                OverwriteConfirmed = overwriteConfirmed
            }
        };

        var itemVm = new RenderJobItemViewModel(job);
        Jobs.Insert(0, itemVm);
        StatusMessage = null;

        _ = RunJobAsync(job, itemVm);
    }

    private async Task RunJobAsync(RenderJob job, RenderJobItemViewModel itemVm)
    {
        try
        {
            var outputPath = await _renderService.RenderAsync(_project, job, itemVm.Token).ConfigureAwait(true);
            StatusMessage = $"Izvoz završen: {outputPath}";
            _logger.Information("Izvoz projekta {ProjectName} završen: {OutputPath} (ffmpeg komanda: {Command})",
                job.ProjectName, outputPath, job.FfmpegCommandLogged);
        }
        catch (OperationCanceledException)
        {
            job.Status = RenderJobStatus.Cancelled;
            StatusMessage = "Izvoz je otkazan.";
            _logger.Information("Izvoz projekta {ProjectName} otkazan (ffmpeg komanda: {Command})", job.ProjectName, job.FfmpegCommandLogged);
        }
        catch (Exception ex)
        {
            // RenderService can throw before it ever sets job.Status away from Queued (e.g. the
            // overwrite-without-confirmation guard) - make sure the queue never shows a stuck "Queued" row.
            if (job.Status is RenderJobStatus.Queued or RenderJobStatus.Running)
            {
                job.Status = RenderJobStatus.Failed;
                job.ErrorMessage ??= ex.Message;
            }

            _logger.Error(ex, "Izvoz projekta {ProjectName} nije uspeo (ffmpeg komanda: {Command})", job.ProjectName, job.FfmpegCommandLogged);
            StatusMessage = $"Izvoz nije uspeo: {ex.Message}";
        }
        finally
        {
            itemVm.RefreshFromJob();
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrEmpty(sanitized) ? "video" : sanitized;
    }
}
