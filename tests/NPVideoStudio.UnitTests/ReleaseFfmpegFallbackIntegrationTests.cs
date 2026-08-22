using System.Diagnostics;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class ReleaseFfmpegFallbackIntegrationTests
{
    [Fact]
    public void CopyFfmpegFromPath_CreatesStandaloneValidatedPayload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoRoot = FindRepositoryRoot();
        var script = Path.Combine(repoRoot, "scripts", "copy-ffmpeg-from-path.ps1");
        Assert.True(File.Exists(script), $"Fallback skripta nije pronađena: {script}");

        var destination = Path.Combine(Path.GetTempPath(), "npvs-ffmpeg-fallback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Destination", destination })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);

            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe", "ffplay.exe" })
            {
                var copied = Path.Combine(destination, name);
                Assert.True(File.Exists(copied), $"Nedostaje {name}");
                Assert.True(new FileInfo(copied).Length >= 1024 * 1024, $"{name} izgleda kao shim, ne stvarni binarni fajl.");
            }
        }
        finally
        {
            try { Directory.Delete(destination, recursive: true); } catch { }
        }
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NPVideoStudio.sln")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Nije pronađen NPVideoStudio.sln iz test radnog direktorijuma.");
    }
}
