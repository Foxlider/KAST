using KAST.Core.Enums;

namespace KAST.Core.Models;

public class HeadlessClient
{
    public int Id { get; set; }
    public int ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;

    public int? ProcessId { get; set; }
    public ServerInstanceStatus Status { get; set; } = ServerInstanceStatus.Stopped;
    public DateTime? StartedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
