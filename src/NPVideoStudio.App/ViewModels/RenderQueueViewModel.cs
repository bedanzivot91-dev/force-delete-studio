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
public sealed record ExportFormatOption(ExportFormat Value, string Label, string Extension);

/// <summary>Export queue with real container/codec/audio-only choices backed by RenderService.</summary>
public sealed partial class RenderQueueViewModel : ViewModelBase, IDisposable
{
    private readonly Project _project;
    private readonly IRenderService _renderService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private bool _outputPathConfirmedForOverwrite;

    public ObservableCollection<RenderJobItemViewModel> Jobs { get; } = new();

    public IReadOnlyList<ExportFormatOption> FormatOptions { get; } = new[]
    {
        new ExportFormatOption(ExportFormat.Mp4, "MP4 video", ".mp4"),
        new ExportFormatOption(ExportFormat.Mov, "MOV video", ".mov"),
        new ExportFormatOption(ExportFormat.WebM, "WebM video", ".webm"),
        new ExportFormatOption(ExportFormat.M4a, "M4A audio (AAC)", ".m4a"),
        new ExportFormatOption(ExportFormat.Mp3, "MP3 audio", ".mp3"),
        new ExportFormatOption(ExportFormat.Wav, "WAV audio (PCM)", ".wav"),
        new ExportFormatOption(ExportFormat.Flac, "FLAC audio (lossless)", ".flac")
    };

    public ObservableCollection<CodecOption> CodecOptions { get; } = new();

    public IReadOnlyList<string> PresetOptions { get; } = new[]
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
    };

    [ObservableProperty]
    private ExportFormatOption _selectedFormatOption = null!;

    [ObservableProperty]
    private CodecOption _selectedCodecOption = null!;

    public bool IsVideoExport => !SelectedFormatOption.Value.IsAudioOnly();
    public bool IsLossyAudioExport => SelectedFormatOption.Value is ExportFormat.M4a or ExportFormat.Mp3 || IsVideoExport;

    public IReadOnlyList<PlatformExportPreset> AvailablePlatformPresets { get; } = PlatformExportPreset.All;

    [ObservableProperty]
    private PlatformExportPreset? _selectedPlatformPreset;

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

        _selectedFormatOption = FormatOptions[0];
        RebuildCodecOptions();

        var directory = string.IsNullOrEmpty(project.ProjectFilePath) ? null : Path.GetDirectoryName(project.ProjectFilePath);
        var defaultName = $"{SanitizeFileName(project.Name)}_captioned{SelectedFormatOption.Extension}";
        OutputFilePath = string.IsNullOrEmpty(directory) ? defaultName : Path.Combine(directory, defaultName);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => RefreshJobs();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    partial void OnSelectedFormatOptionChanged(ExportFormatOption value)
    {
        RebuildCodecOptions();
        EnsureOutputExtension();
        OnPropertyChanged(nameof(IsVideoExport));
        OnPropertyChanged(nameof(IsLossyAudioExport));
        StatusMessage = value.Value.IsAudioOnly()
            ? $"Audio-only izvoz: video se neće upisati u {value.Label}."
            : $"Video format: {value.Label}. Izaberite kompatibilan video kodek.";
    }

    private void RebuildCodecOptions()
    {
        var previous = SelectedCodecOption?.Value;
        CodecOptions.Clear();

        IEnumerable<CodecOption> options = SelectedFormatOption.Value switch
        {
            ExportFormat.Mp4 => new[]
            {
                new CodecOption(VideoCodec.Libx264, "H.264 (libx264 - najkompatibilniji)"),
                new CodecOption(VideoCodec.H264Nvenc, "H.264 NVENC (Nvidia GPU)"),
                new CodecOption(VideoCodec.H264Qsv, "H.264 QSV (Intel GPU)"),
                new CodecOption(VideoCodec.H264Amf, "H.264 AMF (AMD GPU)"),
                new CodecOption(VideoCodec.Libx265, "H.265 / HEVC (libx265)"),
                new CodecOption(VideoCodec.LibaomAv1, "AV1 (libaom-av1)")
            },
            ExportFormat.Mov => new[]
            {
                new CodecOption(VideoCodec.Libx264, "H.264 (libx264)"),
                new CodecOption(VideoCodec.H264Nvenc, "H.264 NVENC (Nvidia GPU)"),
                new CodecOption(VideoCodec.H264Qsv, "H.264 QSV (Intel GPU)"),
                new CodecOption(VideoCodec.H264Amf, "H.264 AMF (AMD GPU)"),
                new CodecOption(VideoCodec.Libx265, "H.265 / HEVC (libx265)")
            },
            ExportFormat.WebM => new[]
            {
                new CodecOption(VideoCodec.LibvpxVp9, "VP9 (libvpx-vp9)"),
                new CodecOption(VideoCodec.LibaomAv1, "AV1 (libaom-av1)")
            },
            _ => Array.Empty<CodecOption>()
        };

        foreach (var option in options)
        {
            CodecOptions.Add(option);
        }

        if (CodecOptions.Count > 0)
        {
            SelectedCodecOption = CodecOptions.FirstOrDefault(x => x.Value.Equals(previous)) ?? CodecOptions[0];
        }
    }

    private void EnsureOutputExtension()
    {
        if (string.IsNullOrWhiteSpace(OutputFilePath))
        {
            return;
        }

        OutputFilePath = Path.ChangeExtension(OutputFilePath, SelectedFormatOption.Extension);
    }

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
        var suggested = string.IsNullOrEmpty(OutputFilePath)
            ? $"izvoz{SelectedFormatOption.Extension}"
            : Path.ChangeExtension(Path.GetFileName(OutputFilePath), SelectedFormatOption.Extension);
        var path = await _storageService.PickSaveFileAsync(
            "Izaberi izlazni fajl za izvoz",
            suggested,
            new[] { (SelectedFormatOption.Label, new[] { SelectedFormatOption.Extension.TrimStart('.') }) });

        if (path is null)
        {
            return;
        }

        OutputFilePath = Path.ChangeExtension(path, SelectedFormatOption.Extension);
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

        EnsureOutputExtension();

        if (File.Exists(OutputFilePath) && !_outputPathConfirmedForOverwrite)
        {
            StatusMessage = "Fajl na toj putanji već postoji - koristite dugme „Izaberi fajl“ da potvrdite prepisivanje, ili unesite drugo ime.";
            return;
        }

        if (IsVideoExport && CodecOptions.Count == 0)
        {
            StatusMessage = "Za izabrani video format nema dostupnog kompatibilnog kodeka.";
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
                Format = SelectedFormatOption.Value,
                Codec = IsVideoExport ? SelectedCodecOption.Value : VideoCodec.Libx264,
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

    partial void OnSelectedPlatformPresetChanged(PlatformExportPreset? value)
    {
        if (value is null || value.Platform == TargetPlatform.Custom)
        {
            return;
        }

        var settings = new RenderSettings { OutputFilePath = OutputFilePath ?? string.Empty };
        value.ApplyTo(settings);

        Crf = settings.Crf;
        Preset = settings.Preset;
        AudioBitrateKbps = settings.AudioBitrateKbps;

        StatusMessage = $"Podešeno za {value.DisplayName}: {value.SummaryLabel}. " +
                        "Veličinu slike određuje format projekta (Novi projekat → Format).";
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
