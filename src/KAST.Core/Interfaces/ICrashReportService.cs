using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ICrashReportService
{
    void RecordProcessStart();
    void RecordCleanShutdown();
    void RecordCrash(Exception exception, string kind, bool isTerminating);
    IReadOnlyList<CrashReportSummary> GetRecentReports(int limit = 10);
}
