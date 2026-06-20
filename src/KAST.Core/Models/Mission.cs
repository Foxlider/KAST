namespace KAST.Core.Models;

public class Mission
{
    public int Id { get; set; }
    public int ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public uint? Hash { get; set; }
    public string PhysicalPath { get; set; } = string.Empty;
    public int? ModPresetId { get; set; }
    public ModPreset? ModPreset { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MissionTagAssignment> TagAssignments { get; set; } = [];
    public ICollection<CampaignMission> CampaignMissions { get; set; } = [];
    public ICollection<SetMission> SetMissions { get; set; } = [];
}
