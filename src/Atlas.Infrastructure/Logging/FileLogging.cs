using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Atlas.Infrastructure.Logging;

/// <summary>Writes diagnostic logs to local files only. They never leave the computer.</summary>
public static class FileLogging
{
    private const int RetainedFileCount = 14;
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    /// <summary>Disposing the returned factory flushes and closes the log file.</summary>
    public static ILoggerFactory CreateLoggerFactory(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logsDirectory, "atlas-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                fileSizeLimitBytes: MaxFileSizeBytes,
                rollOnFileSizeLimit: true,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        return new SerilogLoggerFactory(logger, dispose: true);
    }
}
