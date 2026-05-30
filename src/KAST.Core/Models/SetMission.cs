namespace KAST.Core.Models;

public class SetMission
{
    public int SetId { get; set; }
    public Set Set { get; set; } = null!;
    public int MissionId { get; set; }
    public Mission Mission { get; set; } = null!;
}
