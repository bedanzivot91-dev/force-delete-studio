using Avalonia;
using Serilog;

namespace NPVideoStudio.App;

internal static class Program
{
    // Avalonia needs a static Main below - the AppBuilder is configured here so both the real
    // application entry point and unit/UI tests can reuse the exact same setup.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Last-resort catch: even a startup crash before the logger is wired up must leave a trace,
            // never just silently disappear (spec §43/44).
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
            // Deliberately NOT using Avalonia.Fonts.Inter: it was found to corrupt uppercase
            // caron letters (Š/Č/Ž) at several point sizes - a hard blocker for a Serbian-language
            // app. The platform default font (Segoe UI on Windows) renders Serbian Latin correctly
            // at every size/weight and is what the app now uses everywhere.
            .LogToTrace();
}
