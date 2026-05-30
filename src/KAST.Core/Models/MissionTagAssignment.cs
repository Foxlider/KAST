namespace KAST.Core.Models;

public class MissionTagAssignment
{
    public int MissionId { get; set; }
    public Mission Mission { get; set; } = null!;
    public int MissionTagId { get; set; }
    public MissionTag Tag { get; set; } = null!;
}
