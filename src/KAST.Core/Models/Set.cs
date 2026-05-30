namespace KAST.Core.Models;

public class Set
{
    public int Id { get; set; }
    public int ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SetMission> SetMissions { get; set; } = [];
}
