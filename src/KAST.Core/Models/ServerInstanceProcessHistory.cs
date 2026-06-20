namespace KAST.Core.Models;

public class ServerInstanceProcessHistory
{
    public int Id { get; set; }
    public int ServerInstanceId { get; set; }
    public int ProcessId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string TerminationReason { get; set; } = "Unknown";
    public int? ExitCode { get; set; }

    public ServerInstance ServerInstance { get; set; } = null!;
}
