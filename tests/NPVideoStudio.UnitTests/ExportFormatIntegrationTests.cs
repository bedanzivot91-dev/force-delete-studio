using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NPVideoStudio.App.Views;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// End-to-end export-format proof. These tests do not inspect command strings only: they create a real
/// audio+video source, run the production RenderService through FFmpeg, then ask ffprobe what was actually
/// written. This catches container/codec mismatches that can compile cleanly but fail for users at export.
/// </summary>
public sealed class ExportFormatIntegrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_export_formats_{Guid.NewGuid():N}");

    public ExportFormatIntegrationTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public void RenderQueueView_ExposesRealFormatSelector()
    {
        var view = new RenderQueueView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ComboBox>("ExportFormatComboBox"));
        Assert.NotNull(view.FindControl<StackPanel>("ExportVideoCodecPanel"));

        window.Close();
    }

    [Fact]
    public async Task RenderAsync_RejectsIncompatibleContainerCodecBeforeStartingFfmpeg()
    {
        var service = new RenderService();
        var job = new RenderJob
        {
            ProjectName = "invalid",
            Settings = new RenderSettings
            {
                Format = ExportFormat.WebM,
                Codec = VideoCodec.Libx264,
                OutputFilePath = Path.Combine(_tempDir, "invalid.webm"),
                OverwriteConfirmed = true
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenderAsync(new Project { Name = "invalid" }, job));
        Assert.Contains("nije kompatibilan", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(job.Settings.OutputFilePath));
    }

    [Theory]
    [InlineData(ExportFormat.Mp4, VideoCodec.Libx264, "mp4", "h264", true)]
    [InlineData(ExportFormat.Mov, VideoCodec.Libx264, "mov", "h264", true)]
    [InlineData(ExportFormat.WebM, VideoCodec.LibvpxVp9, "webm", "vp9", true)]
    [InlineData(ExportFormat.M4a, VideoCodec.Libx264, "m4a", "aac", false)]
    [InlineData(ExportFormat.Mp3, VideoCodec.Libx264, "mp3", "mp3", false)]
    [InlineData(ExportFormat.Wav, VideoCodec.Libx264, "wav", "pcm_s16le", false)]
    [InlineData(ExportFormat.Flac, VideoCodec.Libx264, "flac", "flac", false)]
    public async Task RenderAsync_WritesRequestedRealFormat(
        ExportFormat format, VideoCodec codec, string expectedExtension, string expectedCodec, bool expectVideo)
    {
        var sourcePath = await CreateSourceAsync();
        var project = BuildProject(sourcePath);
        var output = Path.Combine(_tempDir, $"render_{Guid.NewGuid():N}.{expectedExtension}");
        var service = new RenderService();
        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings
            {
                Format = format,
                Codec = codec,
                Crf = 30,
                Preset = "veryfast",
                AudioBitrateKbps = 128,
                OutputFilePath = output,
                OverwriteConfirmed = true
            }
        };

        var rendered = await service.RenderAsync(project, job);
        var probe = await ProbeAsync(rendered);

        Assert.Equal(RenderJobStatus.Completed, job.Status);
        Assert.True(File.Exists(rendered));
        Assert.Equal(100, job.ProgressPercent);
        Assert.Contains(expectedCodec, probe.CodecNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectVideo, probe.StreamTypes.Contains("video", StringComparer.OrdinalIgnoreCase));
        Assert.Contains("audio", probe.StreamTypes, StringComparer.OrdinalIgnoreCase);

        if (!expectVideo)
        {
            Assert.DoesNotContain("video", probe.StreamTypes, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("-vn", job.FfmpegCommandLogged ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RenderAsync_Mp4Hevc_WritesHevcStream()
    {
        var sourcePath = await CreateSourceAsync();
        var project = BuildProject(sourcePath);
        var output = Path.Combine(_tempDir, "hevc.mp4");
        var service = new RenderService();
        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings
            {
                Format = ExportFormat.Mp4,
                Codec = VideoCodec.Libx265,
                Crf = 32,
                Preset = "ultrafast",
                AudioBitrateKbps = 96,
                OutputFilePath = output,
                OverwriteConfirmed = true
            }
        };

        await service.RenderAsync(project, job);
        var probe = await ProbeAsync(output);

        Assert.Contains("hevc", probe.CodecNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("video", probe.StreamTypes, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> CreateSourceAsync()
    {
        var path = Path.Combine(_tempDir, "source.mp4");
        if (File.Exists(path)) return path;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var arg in new[]
        {
            "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=0.8:r=10",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=0.8",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", path
        }) process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0 && File.Exists(path), $"Source generation failed: {stderr}");
        return path;
    }

    private static Project BuildProject(string sourcePath)
    {
        var asset = new MediaAsset
        {
            Id = "source",
            FilePath = sourcePath,
            Duration = TimeSpan.FromSeconds(0.8),
            HasVideoStream = true,
            HasAudioStream = true,
            Width = 320,
            Height = 240,
            FrameRate = 10,
            Kind = MediaKind.Video
        };
        var project = new Project
        {
            Name = "Export format integration",
            Format = ProjectFormat.FromPresets(AspectRatioPreset.Widescreen16x9, ResolutionPreset.Hd720, FrameRatePreset.Fps30)
        };
        project.MediaLibrary.Add(asset);
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Name = "Video",
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = asset.Id,
                    SourceTrimInSeconds = 0,
                    SourceTrimOutSeconds = 0.8,
                    TimelineStartSeconds = 0
                }
            }
        });
        return project;
    }

    private static async Task<ProbeResult> ProbeAsync(string path)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var arg in new[]
        {
            "-v", "error", "-show_entries", "stream=codec_name,codec_type", "-of", "json", path
        }) process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, $"ffprobe failed: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var codecs = new List<string>();
        var types = new List<string>();
        foreach (var stream in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            if (stream.TryGetProperty("codec_name", out var codecName)) codecs.Add(codecName.GetString() ?? string.Empty);
            if (stream.TryGetProperty("codec_type", out var codecType)) types.Add(codecType.GetString() ?? string.Empty);
        }
        return new ProbeResult(codecs, types);
    }

    private sealed record ProbeResult(IReadOnlyList<string> CodecNames, IReadOnlyList<string> StreamTypes);
}
