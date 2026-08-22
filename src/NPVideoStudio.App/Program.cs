using Avalonia;
using Serilog;

namespace NPVideoStudio.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // Release verification invokes the actual shipped NPVideoStudio.exe in this headless mode.
            // It runs before Avalonia so the gate proves production persistence + rendering, not only GUI launch.
            if (InstalledProjectSelfTest.TryRun(args, out var selfTestExitCode))
            {
                Environment.ExitCode = selfTestExitCode;
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var crashLogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NP Video Studio", "Logs");
                Directory.CreateDirectory(crashLogDir);
                File.AppendAllText(
                    Path.Combine(crashLogDir, "crash.log"),
                    $"{DateTimeOffset.Now:O} FATAL STARTUP CRASH{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // If we can't even write the crash log, there is nothing more we can safely do.
            }
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Platform default Segoe UI on Windows renders Serbian Latin correctly at every size/weight.
            .LogToTrace();
}
