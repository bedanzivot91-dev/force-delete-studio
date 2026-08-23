using System.Diagnostics;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class ReleaseFfmpegFallbackIntegrationTests
{
    [Fact]
    public void BuildRelease_InitializesFallbackDestinationBeforeNetworkDownload()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "build-release.ps1");
        var script = File.ReadAllText(scriptPath);

        var destinationAssignment = script.IndexOf("$ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'", StringComparison.Ordinal);
        var destinationCreation = script.IndexOf("New-Item -ItemType Directory -Force -Path $ffmpegToolsDir", StringComparison.Ordinal);
        var networkAttempt = script.IndexOf("Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'", StringComparison.Ordinal);
        var fallbackCall = script.IndexOf("& $fallbackScript -Destination $ffmpegToolsDir", StringComparison.Ordinal);

        Assert.True(destinationAssignment >= 0, "Release skripta ne inicijalizuje FFmpeg destination.");
        Assert.True(destinationCreation > destinationAssignment, "FFmpeg destination folder mora da se kreira posle izračunavanja putanje.");
        Assert.True(networkAttempt > destinationCreation, "FFmpeg destination mora postojati pre prvog mrežnog pokušaja.");
        Assert.True(fallbackCall > networkAttempt, "Fallback poziv mora ostati posle primarnog download pokušaja.");

        var assignmentsBeforeNetwork = script[..networkAttempt].Split("$ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, assignmentsBeforeNetwork);
    }

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
