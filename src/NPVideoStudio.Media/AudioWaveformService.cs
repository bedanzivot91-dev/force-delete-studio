using System.Diagnostics;
using System.Globalization;

namespace NPVideoStudio.Media;

public interface IAudioWaveformService
{
    Task<IReadOnlyList<double>> ExtractPeaksAsync(string sourceFilePath, double trimInSeconds,
        double trimDurationSeconds, int peakCount = 256, CancellationToken cancellationToken = default);
}

/// <summary>Decodes a lightweight mono PCM stream and reduces it to normalized peak amplitudes for the timeline.</summary>
public sealed class AudioWaveformService : IAudioWaveformService
{
    private const int SampleRate = 8000;
    private readonly string _ffmpegPath;

    public AudioWaveformService(string? ffmpegOverridePath = null) =>
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);

    public async Task<IReadOnlyList<double>> ExtractPeaksAsync(string sourceFilePath, double trimInSeconds,
        double trimDurationSeconds, int peakCount = 256, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath) || trimDurationSeconds <= 0 || peakCount <= 0)
            return Array.Empty<double>();

        peakCount = Math.Clamp(peakCount, 32, 1024);
        var expectedSamples = Math.Max(1L, (long)Math.Ceiling(trimDurationSeconds * SampleRate));
        var samplesPerPeak = Math.Max(1L, (long)Math.Ceiling(expectedSamples / (double)peakCount));
        var peaks = new double[peakCount];

        using var process = new Process { StartInfo = new ProcessStartInfo {
            FileName = _ffmpegPath, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true } };
        void Arg(string value) => process.StartInfo.ArgumentList.Add(value);
        Arg("-hide_banner"); Arg("-loglevel"); Arg("error");
        Arg("-ss"); Arg(Math.Max(0, trimInSeconds).ToString(CultureInfo.InvariantCulture));
        Arg("-i"); Arg(sourceFilePath);
        Arg("-t"); Arg(trimDurationSeconds.ToString(CultureInfo.InvariantCulture));
        Arg("-vn"); Arg("-ac"); Arg("1"); Arg("-ar"); Arg(SampleRate.ToString(CultureInfo.InvariantCulture));
        Arg("-f"); Arg("s16le"); Arg("-acodec"); Arg("pcm_s16le"); Arg("-");

        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        { return Array.Empty<double>(); }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var buffer = new byte[16 * 1024];
        long sampleIndex = 0;
        try
        {
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                for (var offset = 0; offset + 1 < read; offset += 2)
                {
                    var sample = (short)(buffer[offset] | buffer[offset + 1] << 8);
                    var bucket = (int)Math.Min(peakCount - 1, sampleIndex / samplesPerPeak);
                    peaks[bucket] = Math.Max(peaks[bucket], Math.Abs(sample / 32768.0));
                    sampleIndex++;
                }
            }
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return process.ExitCode == 0 && sampleIndex > 0 ? peaks : Array.Empty<double>();
    }
}
