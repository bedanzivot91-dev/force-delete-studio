using NPVideoStudio.Domain;
using Serilog;
using Serilog.Events;

namespace NPVideoStudio.Infrastructure.Logging;

/// <summary>
/// Configures structured, per-module log files under %LocalAppData%/NP Video Studio/Logs (spec §44).
/// Separate files per module keep an FFmpeg failure from burying an application-level warning.
/// </summary>
public static class AppLogging
{
    public static ILogger CreateLogger(int retentionDays)
    {
        var logsFolder = AppSettings.LogsFolder();
        Directory.CreateDirectory(logsFolder);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("Application", "NP Video Studio")
            .WriteTo.File(
                Path.Combine(logsFolder, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileTimeLimit: TimeSpan.FromDays(Math.Max(1, retentionDays)),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}",
                shared: true)
            .WriteTo.File(
                Path.Combine(logsFolder, "errors-.log"),
                restrictedToMinimumLevel: LogEventLevel.Warning,
                rollingInterval: RollingInterval.Day,
                retainedFileTimeLimit: TimeSpan.FromDays(Math.Max(1, retentionDays)),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();
    }
}
