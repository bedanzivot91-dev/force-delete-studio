using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Runs FFmpeg/libvidstab's first pass for every picture clip that will actually be rendered with
/// stabilization enabled. Motion vectors are disposable render artifacts: only user-facing settings are
/// persisted in the project; .trf analysis files live under the OS temp folder and are always deleted.
/// </summary>
public static class VideoStabilizationPrepass
{
    public static async Task<StabilizationPrepassContext> PrepareAsync(
        Project project,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);

        var clips = EnumerateRenderedPictureClips(project)
            .Where(c => c.StabilizationEnabled)
            .DistinctBy(c => c.Id)
            .ToArray();

        if (clips.Length == 0)
        {
            return StabilizationPrepassContext.Empty;
        }

        var root = Path.Combine(Path.GetTempPath(), $"npvs-stabilization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var transforms = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var clip in clips)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (clip.IsFreezeFrame || clip.IsReversed)
                {
                    throw new InvalidOperationException(
                        "Stabilizacija ne može istovremeno sa Reverse/Freeze Frame na istom klipu. " +
                        "Isključite Reverse/Freeze ili stabilizaciju.");
                }

                if (clip.MediaAssetId is null)
                {
                    throw new InvalidOperationException("Stabilizovan klip nema izvorni medij.");
                }

                var asset = project.MediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId);
                if (asset is null || !asset.HasVideoStream || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
                {
                    throw new InvalidOperationException(
                        $"Originalni video za stabilizovani klip nije dostupan (MediaAssetId: {clip.MediaAssetId}).");
                }

                var sourceDuration = clip.SourceTrimOutSeconds - clip.SourceTrimInSeconds;
                if (sourceDuration <= 0.05)
                {
                    throw new InvalidOperationException("Klip je prekratak za stabilizaciju.");
                }

                var transformPath = Path.Combine(root, $"{clip.Id}.trf");
                await RunDetectAsync(
                        ffmpegPath, asset.FilePath, clip, transformPath,
                        project.Format.Width, project.Format.Height, cancellationToken)
                    .ConfigureAwait(false);

                if (!File.Exists(transformPath) || new FileInfo(transformPath).Length == 0)
                {
                    throw new InvalidOperationException(
                        $"FFmpeg vidstabdetect je završen bez upotrebljivog motion fajla za klip {clip.Id}.");
                }

                transforms[clip.Id] = transformPath;
            }

            return new StabilizationPrepassContext(root, transforms);
        }
        catch
        {
            TryDeleteDirectory(root);
            throw;
        }
    }

    private static IEnumerable<TimelineClip> EnumerateRenderedPictureClips(Project project)
    {
        var baseVideoTrack = project.Timeline.Tracks
            .FirstOrDefault(t => t.Kind == TimelineTrackKind.Video && t.Clips.Count > 0);

        if (baseVideoTrack is not null)
        {
            foreach (var clip in baseVideoTrack.Clips)
            {
                yield return clip;
            }
        }

        foreach (var track in project.Timeline.Tracks.Where(t => !t.IsHidden))
        {
            if (ReferenceEquals(track, baseVideoTrack))
            {
                continue;
            }

            if (track.Kind is not (TimelineTrackKind.Video or TimelineTrackKind.ImageOverlay))
            {
                continue;
            }

            foreach (var clip in track.Clips)
            {
                yield return clip;
            }
        }
    }

    private static async Task RunDetectAsync(
        string ffmpegPath,
        string sourcePath,
        TimelineClip clip,
        string transformPath,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var duration = clip.SourceTrimOutSeconds - clip.SourceTrimInSeconds;
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(clip.SourceTrimInSeconds.ToString("0.########", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(duration.ToString("0.########", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-vf");

        var detectFilter =
            $"vidstabdetect=result='{FfmpegFilterGraphBuilder.EscapeFilterPath(transformPath)}':shakiness={Math.Clamp(clip.StabilizationShakiness, 1, 10)}:accuracy={Math.Clamp(clip.StabilizationAccuracy, 1, 15)}";
        if (clip.AutoReframeEnabled)
        {
            // Tracking coordinates are authored against the original source frame. The final render first
            // resets the trimmed clip's clock and crops around that tracking path. Detect motion on those
            // exact same reframed pixels/dimensions, otherwise vidstabtransform would consume vectors from
            // a different geometry and the combined feature could drift or fail.
            detectFilter = "setpts=PTS-STARTPTS" +
                           FfmpegFilterGraphBuilder.BuildAutoReframeFilter(clip, targetWidth, targetHeight) +
                           "," + detectFilter;
        }
        startInfo.ArgumentList.Add(detectFilter);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("-");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException("FFmpeg nije pronađen za stabilization pre-pass.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort process cleanup; cancellation/original error remains authoritative.
                }
            }
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"FFmpeg vidstabdetect nije uspeo za klip {clip.Id} (kod {process.ExitCode}). {detail.Trim()}");
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class StabilizationPrepassContext : IDisposable
{
    private readonly string? _root;
    public static StabilizationPrepassContext Empty { get; } = new(null, new Dictionary<string, string>());

    public IReadOnlyDictionary<string, string> TransformFiles { get; }

    internal StabilizationPrepassContext(string? root, IReadOnlyDictionary<string, string> transformFiles)
    {
        _root = root;
        TransformFiles = transformFiles;
    }

    public void Dispose()
    {
        if (_root is not null)
        {
            VideoStabilizationPrepass.TryDeleteDirectory(_root);
        }
    }
}
