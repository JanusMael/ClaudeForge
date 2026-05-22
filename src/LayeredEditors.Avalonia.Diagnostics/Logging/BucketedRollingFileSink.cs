using System.Globalization;
using System.Text;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;

/// <summary>
/// A Serilog <see cref="ILogEventSink"/> that rolls log files in fixed-size
/// UTC buckets (default: 8 hours → bucket starts 00:00, 08:00, 16:00) and
/// prunes files older than a configurable retention window (default: 3 days).
/// <para>
/// Serilog's built-in <c>RollingInterval</c> only supports Hour / Day / Month,
/// none of which match the "three files per day, three days retained"
/// cadence most Avalonia apps want for support-bundle logs. This wrapper
/// computes the current bucket manually and delegates to a fresh
/// <c>Serilog.Sinks.File</c> logger on each bucket transition. Bucket
/// transitions are guarded by a <c>lock</c>.
/// </para>
/// <para>
/// File naming convention: <c>{prefix}-yyyyMMdd-HH.txt</c> where <c>HH</c> is
/// the bucket-start hour in UTC. A retention sweep runs on construction and
/// on each rotation; it deletes files whose embedded bucket stamp is older
/// than <see cref="Retention"/>.
/// </para>
/// <para>
/// <strong>Bucket-size constraints.</strong>
/// <see cref="TimeSpan.TotalHours"/> must be a positive whole number that
/// divides 24 evenly (1, 2, 3, 4, 6, 8, 12, 24). Any other value throws
/// <see cref="ArgumentOutOfRangeException"/> — otherwise bucket boundaries
/// would drift across midnight UTC and filenames would not sort.
/// </para>
/// </summary>
public sealed class BucketedRollingFileSink : ILogEventSink, IDisposable
{
    /// <summary>Default bucket size: 8 hours (three files per day).</summary>
    public static TimeSpan DefaultBucketSize { get; } = TimeSpan.FromHours(8);

    /// <summary>Default retention: 3 days.</summary>
    public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(3);

    /// <summary>Default filename prefix: <c>"app"</c>.</summary>
    public const string DefaultFileNamePrefix = "app";

    private static readonly MessageTemplateTextFormatter _formatter =
        new("{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            formatProvider: null);

    private readonly string _logsDirectory;
    private readonly Func<DateTime> _clock;
    private readonly int _bucketHours;
    private readonly Lock _lock = new();

    /// <summary>Retention window. Files whose bucket stamp is older than
    /// <c>_clock().Date - Retention</c> are pruned.</summary>
    public TimeSpan Retention { get; }

    /// <summary>Bucket size the sink was constructed with.</summary>
    public TimeSpan BucketSize => TimeSpan.FromHours(_bucketHours);

    /// <summary>Filename prefix the sink was constructed with (no dashes).</summary>
    public string FileNamePrefix { get; }

    /// <summary>
    /// Full path of the file currently being written, or <c>null</c> before the
    /// first event is emitted. Updated on every rotation. Useful for UI chrome
    /// that wants to display a "current log file" link.
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    private ILogger? _currentLogger;
    private DateTime _currentBucketStart = DateTime.MinValue;

    /// <summary>
    /// Creates the sink backed by <paramref name="logsDirectory"/>.
    /// </summary>
    /// <param name="logsDirectory">Directory where log files are written.
    /// The caller is responsible for ensuring it exists.</param>
    /// <param name="bucketSize">Size of each rolling bucket; must be a whole
    /// number of hours that divides 24 evenly. <c>null</c> uses
    /// <see cref="DefaultBucketSize"/>.</param>
    /// <param name="retention">Files older than this are deleted on startup and
    /// on every rotation. <c>null</c> uses <see cref="DefaultRetention"/>.</param>
    /// <param name="fileNamePrefix">Filename prefix (e.g. <c>"app"</c> produces
    /// <c>"app-20260422-08.txt"</c>). <c>null</c> uses
    /// <see cref="DefaultFileNamePrefix"/>. Must not contain path separators,
    /// whitespace, or <c>-</c>.</param>
    /// <param name="clock">Optional clock override used by tests. Defaults to
    /// <see cref="DateTime.UtcNow"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="bucketSize"/> is not a whole number of hours
    /// that divides 24, or when <paramref name="retention"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fileNamePrefix"/> contains invalid characters.
    /// </exception>
    public BucketedRollingFileSink(
        string logsDirectory,
        TimeSpan? bucketSize = null,
        TimeSpan? retention = null,
        string? fileNamePrefix = null,
        Func<DateTime>? clock = null)
    {
        _logsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
        _clock = clock ?? (() => DateTime.UtcNow);

        TimeSpan size = bucketSize ?? DefaultBucketSize;
        if (size.Ticks <= 0 || size.Ticks % TimeSpan.TicksPerHour != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize),
                size, "Bucket size must be a positive whole number of hours.");
        }

        int hours = (int)size.TotalHours;
        if (hours > 24 || 24 % hours != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize),
                size, "Bucket size in hours must divide 24 evenly (1, 2, 3, 4, 6, 8, 12, 24).");
        }

        _bucketHours = hours;

        Retention = retention ?? DefaultRetention;
        if (Retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), Retention,
                "Retention must be positive.");
        }

        string prefix = fileNamePrefix ?? DefaultFileNamePrefix;
        if (string.IsNullOrWhiteSpace(prefix)
            || prefix.Contains('-')
            || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "File name prefix must be non-empty, contain no '-' and no invalid filename chars.",
                nameof(fileNamePrefix));
        }

        FileNamePrefix = prefix;

        // Clean any stale files from a previous session before emitting anything.
        PruneOldFiles();
    }

    // -----------------------------------------------------------------------
    // ILogEventSink
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
    {
        DateTime now = _clock();
        DateTime bucketStart = BucketStart(now);

        lock (_lock)
        {
            if (bucketStart != _currentBucketStart)
            {
                Rotate(bucketStart);
            }

            StringWriter writer = new();
            _formatter.Format(logEvent, writer);
            _currentLogger?.Information("{RawLine}", writer.ToString().TrimEnd());
        }
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            (_currentLogger as IDisposable)?.Dispose();
            _currentLogger = null;
        }
    }

    // -----------------------------------------------------------------------
    // Bucket helpers (static — parameterized on bucket size for testability)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the UTC <see cref="DateTime"/> at which the bucket containing
    /// <paramref name="utcNow"/> started.
    /// </summary>
    internal DateTime BucketStart(DateTime utcNow)
    {
        return BucketStart(utcNow, _bucketHours);
    }

    /// <summary>
    /// Floors <paramref name="utcNow"/> to the start of its bucket given a
    /// bucket size in whole hours. Public for tests; callers should normally
    /// use the instance <see cref="BucketStart(DateTime)"/> overload.
    /// </summary>
    internal static DateTime BucketStart(DateTime utcNow, int bucketHours)
    {
        return new DateTime(utcNow.Year, utcNow.Month, utcNow.Day,
            utcNow.Hour / bucketHours * bucketHours, 0, 0,
            DateTimeKind.Utc);
    }

    /// <summary>
    /// Returns the filename for the bucket containing <paramref name="utcNow"/>
    /// using this sink's configured prefix and bucket size.
    /// </summary>
    public string FileName(DateTime utcNow)
    {
        return FileName(FileNamePrefix, BucketStart(utcNow));
    }

    internal static string FileName(string prefix, DateTime bucketStart)
    {
        return $"{prefix}-{bucketStart:yyyyMMdd}-{bucketStart.Hour:D2}.txt";
    }

    // -----------------------------------------------------------------------
    // Rotation & retention
    // -----------------------------------------------------------------------

    private void Rotate(DateTime newBucketStart)
    {
        // Dispose the previous logger first so its file handle is released
        // before we open the next one.
        (_currentLogger as IDisposable)?.Dispose();
        _currentLogger = null;

        string filePath = Path.Combine(_logsDirectory, FileName(FileNamePrefix, newBucketStart));

        // explicit UTF-8 WITHOUT BOM.  Serilog.Sinks.File 6.0's
        // null-default for `encoding` is already UTF-8 without BOM, but a
        // Linux smoke pass (CachyOS) reported the file showing as binary in
        // a text editor.  Pinning the encoding here removes any ambiguity
        // about what bytes hit the disk — and lets us future-proof against
        // a Serilog.Sinks.File default change.
        UTF8Encoding utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        _currentLogger = new LoggerConfiguration()
                         .MinimumLevel.Information()
                         .WriteTo.File(
                             filePath,
                             outputTemplate: "{Message:lj}{NewLine}",
                             encoding: utf8NoBom,
                             shared: false,
                             flushToDiskInterval: TimeSpan.FromSeconds(2),
                             fileSizeLimitBytes: 4194304,
                             rollOnFileSizeLimit: true)
                         .CreateLogger();

        _currentBucketStart = newBucketStart;
        CurrentFilePath = filePath;

        // Clean up stale files whenever we roll — opportunistic and cheap.
        PruneOldFiles();
    }

    private void PruneOldFiles()
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
            {
                return;
            }

            DateTime cutoff = _clock().Date - Retention;
            string pattern = $"{FileNamePrefix}-*.txt";

            foreach (string file in Directory.GetFiles(_logsDirectory, pattern))
            {
                if (TryParseBucketStamp(FileNamePrefix, Path.GetFileName(file), out DateTime stamp)
                    && stamp < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // File locked or no permission — skip this iteration; the next
                        // retention pass will pick it up if/when the lock releases.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retention sweep must never crash the logger. Filesystem issues here
            // (logs directory removed mid-sweep, perms changed) just abort this pass.
        }
    }

    /// <summary>
    /// Parses the date portion of a bucket filename
    /// (<c>{prefix}-yyyyMMdd-HH.txt</c>) and returns the UTC
    /// <see cref="DateTime"/> of the bucket's day at midnight. Returns
    /// <c>false</c> for unrecognised filenames.
    /// </summary>
    internal static bool TryParseBucketStamp(string prefix, string fileName, out DateTime stamp)
    {
        stamp = default;

        // Expected: "{prefix}-yyyyMMdd-HH.txt".
        // Length sanity: prefix + '-' + 8 + '-' + 2 + ".txt" = prefix.Length + 16.
        if (fileName.Length != prefix.Length + 16)
        {
            return false;
        }

        if (!fileName.StartsWith(prefix + "-", StringComparison.Ordinal))
        {
            return false;
        }

        if (!fileName.EndsWith(".txt", StringComparison.Ordinal))
        {
            return false;
        }

        string datePart = fileName.Substring(prefix.Length + 1, 8); // "20260422"
        return DateTime.TryParseExact(
            datePart,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out stamp);
    }
}