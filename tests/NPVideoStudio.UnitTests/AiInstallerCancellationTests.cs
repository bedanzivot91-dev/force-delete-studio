using System.Diagnostics;
using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class AiInstallerCancellationTests
{
    [Fact]
    public async Task InstallSongAiAsync_CancellationKillsPowerShellInsteadOfLeavingInstallerRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), "npvs-ai-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var script = Path.Combine(temp, "long-install.ps1");
        var pidFile = Path.Combine(temp, "installer.pid");
        var completedFile = Path.Combine(temp, "completed.txt");

        static string PsQuote(string value) => value.Replace("'", "''");
        await File.WriteAllTextAsync(script,
            $"Set-Content -LiteralPath '{PsQuote(pidFile)}' -Value $PID\r\n" +
            "Start-Sleep -Seconds 30\r\n" +
            $"Set-Content -LiteralPath '{PsQuote(completedFile)}' -Value 'should-not-exist'\r\n");

        try
        {
            var client = new AiWorkerClient(installerScriptOverridePath: script);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.InstallSongAiAsync(cancellationToken: cts.Token));

            // Give Windows a short window to reap the process tree. A broken implementation would keep
            // sleeping for 30 seconds and eventually create completed.txt despite the UI reporting cancel.
            await Task.Delay(800);
            Assert.False(File.Exists(completedFile));

            if (File.Exists(pidFile) && int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out var pid))
            {
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}
