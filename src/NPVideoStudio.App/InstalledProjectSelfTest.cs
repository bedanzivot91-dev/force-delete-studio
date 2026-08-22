using System.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;

namespace NPVideoStudio.App;

/// <summary>
/// Headless production-path smoke used only by the installed-release gate. It deliberately exercises the
/// same ProjectRepository + RenderService shipped in NPVideoStudio.exe: create real media, save a real
/// .npvsproject, load it again, then export through the normal FFmpeg timeline renderer.
/// </summary>
internal static class InstalledProjectSelfTest
{
    private const string Switch = "--self-test-project-render";

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], Switch, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                throw new ArgumentException($"Upotreba: {Switch} <radni-folder>");
            }

            RunAsync(Path.GetFullPath(args[1])).GetAwaiter().GetResult();
            exitCode = 0;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine(ex); } catch { }
            exitCode = 91;
        }

        return true;
    }

    private static async Task RunAsync(string workDir)
    {
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "npvs-installed-e2e-source.mp4");
        var projectPath = Path.Combine(workDir, "npvs-installed-e2e.npvsproject");
        var outputPath = Path.Combine(workDir, "npvs-installed-app-render.mp4");
        var markerPath = Path.Combine(workDir, "npvs-installed-e2e.success.txt");

        foreach (var path in new[] { sourcePath, projectPath, outputPath, markerPath })
        {
            if (File.Exists(path)) File.Delete(path);
        }

        var ffmpeg = FfmpegLocator.ResolveFfmpegPath(null);
        await CreateSyntheticSourceAsync(ffmpeg, sourcePath).ConfigureAwait(false);
        if (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length < 1_000)
        {
            throw new InvalidOperationException("NP installed E2E nije napravio validan izvorni MP4.");
        }

        var videoAsset = new MediaAsset
        {
            FilePath = sourcePath,
            Kind = MediaKind.Video,
            Duration = TimeSpan.FromSeconds(1.2),
            Width = 320,
            Height = 180,
            Fps = 30,
            VideoCodec = "h264",
            AudioCodec = "aac",
            HasVideoStream = true,
            HasAudioStream = true,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };

        var project = new Project
        {
            Name = "NP Installed E2E",
            Format = new ProjectFormat
            {
                AspectRatio = AspectRatioPreset.Widescreen16x9,
                Resolution = ResolutionPreset.Hd720,
                FrameRate = FrameRatePreset.Fps30,
                Width = 320,
                Height = 180,
                Fps = 30
            }
        };
        project.MediaLibrary.Add(videoAsset);
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Name = "Video",
            Clips = new List<TimelineClip>
            {
                new()
                {
                    MediaAssetId = videoAsset.Id,
                    SourceTrimInSeconds = 0,
                    SourceTrimOutSeconds = 1.2,
                    TimelineStartSeconds = 0,
                    Brightness = 0.05,
                    Contrast = 1.05,
                    FadeInSeconds = 0.10,
                    FadeOutSeconds = 0.10
                }
            }
        });
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Text,
            Name = "Tekst",
            Clips = new List<TimelineClip>
            {
                new()
                {
                    TextContent = "NP E2E",
                    SourceTrimInSeconds = 0,
                    SourceTrimOutSeconds = 1.2,
                    TimelineStartSeconds = 0,
                    FontSizePx = 28,
                    TextColor = "#FFFFFF",
                    HasTextBackground = true,
                    TextBackgroundColor = "#000000",
                    TextBackgroundOpacity = 0.55,
                    TextPosition = CaptionTextPosition.Middle
                }
            }
        });

        var repository = new ProjectRepository();
        await repository.SaveAsync(project, projectPath).ConfigureAwait(false);
        var loaded = await repository.LoadAsync(projectPath).ConfigureAwait(false);

        if (loaded.MediaLibrary.Count != 1 || loaded.Timeline.Tracks.Count != 2 ||
            loaded.Timeline.Tracks.Sum(t => t.Clips.Count) != 2 ||
            !string.Equals(loaded.ProjectFilePath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("NP installed E2E projekat se nije lossless sačuvao/ponovo učitao.");
        }

        var job = new RenderJob
        {
            ProjectName = loaded.Name,
            Settings = new RenderSettings
            {
                Format = ExportFormat.Mp4,
                Codec = VideoCodec.Libx264,
                Crf = 28,
                Preset = "ultrafast",
                AudioBitrateKbps = 128,
                OutputFilePath = outputPath,
                OverwriteConfirmed = true
            }
        };

        var renderer = new RenderService();
        var rendered = await renderer.RenderAsync(loaded, job).ConfigureAwait(false);
        if (job.Status != RenderJobStatus.Completed || job.ProgressPercent < 99.9 ||
            !string.Equals(rendered, outputPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(outputPath) || new FileInfo(outputPath).Length < 1_000)
        {
            throw new InvalidOperationException("NP production RenderService nije napravio validan instalirani-app render.");
        }

        await File.WriteAllTextAsync(markerPath,
            $"NP INSTALLED PROJECT E2E PASSED{Environment.NewLine}project={projectPath}{Environment.NewLine}render={outputPath}{Environment.NewLine}")
            .ConfigureAwait(false);
    }

    private static async Task CreateSyntheticSourceAsync(string ffmpeg, string outputPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        foreach (var arg in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y",
                     "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=30:d=1.2",
                     "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=1.2",
                     "-shortest", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", outputPath
                 })
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (!process.Start()) throw new InvalidOperationException("NP installed E2E nije mogao da pokrene bundled FFmpeg.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"NP installed E2E source FFmpeg je pao ({process.ExitCode}): {stderr}");
        }
    }
}
