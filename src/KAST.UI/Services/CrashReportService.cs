using System.Reflection;
using System.Text.Json;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.UI.Services;

public sealed class CrashReportService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    KastLogStore logStore,
    IOutputSanitizer sanitizer) : ICrashReportService
{
    private const int MaxLogExcerptEntries = 100;
    private readonly object _lock = new();

    private string CrashRoot => ResolvePath(configuration["Kast:CrashReportsDirectory"] ?? ".KAST_DATA/crash-reports");
    private string MarkerPath => Path.Combine(CrashRoot, "current-run.json");

    public void RecordProcessStart()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(CrashRoot);
            if (File.Exists(MarkerPath))
            {
                try
                {
                    var previous = File.ReadAllText(MarkerPath);
                    WriteReport(new CrashReportDocument(
                        NewId("unclean-shutdown"),
                        DateTimeOffset.UtcNow,
                        "UncleanShutdown",
                        Environment.ProcessId,
                        GetVersion(),
                        "KAST started after the previous process ended without a clean shutdown marker.",
                        null,
                        null,
                        false,
                        previous,
                        GetLogExcerpt()));
                }
                catch
                {
                    // Crash reporting must never prevent startup.
                }
            }

            var marker = new RunMarker(
                DateTimeOffset.UtcNow,
                Environment.ProcessId,
                GetVersion(),
                Environment.ProcessPath,
                Environment.CommandLine,
                false);
            File.WriteAllText(MarkerPath, JsonSerializer.Serialize(marker, JsonOptions));
        }
    }

    public void RecordCleanShutdown()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(MarkerPath))
                    File.Delete(MarkerPath);
            }
            catch
            {
                // Best effort. A stale marker is preferable to blocking shutdown.
            }
        }
    }

    public void RecordCrash(Exception exception, string kind, bool isTerminating)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(CrashRoot);
                WriteReport(new CrashReportDocument(
                    NewId(kind),
                    DateTimeOffset.UtcNow,
                    kind,
                    Environment.ProcessId,
                    GetVersion(),
                    sanitizer.SanitizeException(exception),
                    exception.GetType().FullName,
                    sanitizer.Sanitize(exception.StackTrace ?? ""),
                    isTerminating,
                    TryReadMarker(),
                    GetLogExcerpt()));
            }
            catch
            {
                // Nothing useful can be done safely from a crash handler.
            }
        }
    }

    public IReadOnlyList<CrashReportSummary> GetRecentReports(int limit = 10)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(CrashRoot);
            return Directory.EnumerateFiles(CrashRoot, "*.json")
                .Where(path => !string.Equals(path, MarkerPath, StringComparison.OrdinalIgnoreCase))
                .Select(ReadSummary)
                .Where(summary => summary is not null)
                .Cast<CrashReportSummary>()
                .OrderByDescending(summary => summary.CreatedAt)
                .Take(Math.Clamp(limit, 1, 100))
                .ToList();
        }
    }

    private CrashReportSummary? ReadSummary(string path)
    {
        try
        {
            var document = JsonSerializer.Deserialize<CrashReportDocument>(File.ReadAllText(path), JsonOptions);
            return document is null
                ? null
                : new CrashReportSummary(
                    document.Id,
                    document.CreatedAt,
                    document.Kind,
                    document.ProcessId,
                    document.Message,
                    sanitizer.ToDisplayPath(path));
        }
        catch
        {
            return null;
        }
    }

    private void WriteReport(CrashReportDocument document)
    {
        var path = Path.Combine(CrashRoot, document.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    private IReadOnlyList<string> GetLogExcerpt()
        => logStore.GetAll()
            .TakeLast(MaxLogExcerptEntries)
            .Select(entry => $"{entry.Timestamp:O} [{entry.Level}] {entry.Category}: {entry.Message}")
            .ToList();

    private string? TryReadMarker()
    {
        try
        {
            return File.Exists(MarkerPath) ? File.ReadAllText(MarkerPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private string ResolvePath(string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));

    private static string NewId(string kind)
        => $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{SanitizeFileToken(kind)}-{Guid.NewGuid():N}";

    private static string SanitizeFileToken(string value)
    {
        var safe = new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "crash" : safe.Trim('-').ToLowerInvariant();
    }

    private static string GetVersion()
        => Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "dev";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed record RunMarker(
        DateTimeOffset StartedAt,
        int ProcessId,
        string Version,
        string? ProcessPath,
        string CommandLine,
        bool CleanShutdown);

    private sealed record CrashReportDocument(
        string Id,
        DateTimeOffset CreatedAt,
        string Kind,
        int ProcessId,
        string Version,
        string Message,
        string? ExceptionType,
        string? StackTrace,
        bool IsTerminating,
        string? PreviousRunMarker,
        IReadOnlyList<string> LogExcerpt);
}
