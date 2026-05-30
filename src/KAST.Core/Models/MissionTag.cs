namespace KAST.Core.Models;

public class MissionTag
{
    public int Id { get; set; }
    public int ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;
    public string Name { get; set; } = string.Empty;

    public ICollection<MissionTagAssignment> MissionAssignments { get; set; } = [];
}
