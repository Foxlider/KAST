namespace KAST.Core.Models;

public class CampaignMission
{
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;
    public int MissionId { get; set; }
    public Mission Mission { get; set; } = null!;
    public int OrderIndex { get; set; }
}
