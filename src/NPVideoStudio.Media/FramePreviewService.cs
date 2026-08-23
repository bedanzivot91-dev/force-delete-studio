using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Core.Services;

namespace NPVideoStudio.Media;

/// <summary>
/// Real ffmpeg-based single-frame extraction (`-ss &lt;t&gt; -i &lt;file&gt; -frames:v 1 -f image2pipe
/// -vcodec png -`, read from stdout) - verified empirically before writing this: a real lavfi test video
/// piped through this exact command produced a valid, correctly-sized decodable PNG every time. No temp
/// file needed (unlike <see cref="RenderService"/>'s output, which is a real deliverable that must
/// survive a crash mid-write) - a preview frame that fails to arrive just means the UI keeps showing the
/// previous one, so an in-memory pipe is the simpler, equally real choice here.
/// </summary>
public sealed class FramePreviewService : IFramePreviewService
{
    private readonly string _ffmpegPath;

    public FramePreviewService(string? ffmpegOverridePath = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public async Task<byte[]?> ExtractFrameAsync(string sourceFilePath, double timestampSeconds, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            return null;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(Math.Max(0, timestampSeconds).ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(sourceFilePath);
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("image2pipe");
        process.StartInfo.ArgumentList.Add("-vcodec");
        process.StartInfo.ArgumentList.Add("png");
        process.StartInfo.ArgumentList.Add("-");

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return null;
        }

        using var memoryStream = new MemoryStream();
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* best-effort on cancellation */ }
            }

            throw;
        }

        await stdErrTask.ConfigureAwait(false);
        return process.ExitCode == 0 && memoryStream.Length > 0 ? memoryStream.ToArray() : null;
    }
}
