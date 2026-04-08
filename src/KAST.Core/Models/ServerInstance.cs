using KAST.Core.Enums;

namespace KAST.Core.Models;

public class ServerInstance
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public int Port { get; set; } = 2302;
    public int SteamQueryPort { get; set; } = 2303;

    public ServerInstanceStatus Status { get; set; } = ServerInstanceStatus.Stopped;
    public RestartPolicy RestartPolicy { get; set; } = RestartPolicy.None;
    public int MaxRestartAttempts { get; set; } = 3;

    // Scheduling
    public string? AutoStartTime { get; set; }  // "HH:mm" format
    public string? AutoStopTime { get; set; }    // "HH:mm" format
    public bool ScheduleEnabled { get; set; }

    // Arma 3 config files (raw text stored in DB)
    public string? ServerCfgContent { get; set; }
    public string? BasicCfgContent { get; set; }
    public string? ArmaProfileContent { get; set; }
    public string? AdditionalParameters { get; set; }

    // Creator DLC toggles
    public bool ContactDlc { get; set; }
    public bool GmDlc { get; set; }
    public bool PfDlc { get; set; }
    public bool CslaDlc { get; set; }
    public bool WsDlc { get; set; }
    public bool SpeDlc { get; set; }
    public bool RfDlc { get; set; }
    public bool EfDlc { get; set; }

    // Installation tracking
    public DateTime? InstalledAt { get; set; }
    public string? InstalledBuildId { get; set; }

    // Process tracking
    public int? ProcessId { get; set; }
    public DateTime? StartedAt { get; set; }

    // Headless clients
    public int HeadlessClientCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }

    public ICollection<ServerInstanceMod> Mods { get; set; } = [];
    public ICollection<HeadlessClient> HeadlessClients { get; set; } = [];
}
