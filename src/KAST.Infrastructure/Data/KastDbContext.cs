using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace KAST.Infrastructure.Data;

public class KastDbContext : DbContext
{
    public KastDbContext(DbContextOptions<KastDbContext> options) : base(options)
    {
        // DbContext configuration is supplied entirely through DI.
    }

    public DbSet<SteamMod> Mods => Set<SteamMod>();
    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
    public DbSet<ServerInstanceMod> ServerInstanceMods => Set<ServerInstanceMod>();
    public DbSet<HeadlessClient> HeadlessClients => Set<HeadlessClient>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<KastUser> Users => Set<KastUser>();
    public DbSet<DownloadTask> DownloadTasks => Set<DownloadTask>();
    public DbSet<KastSettings> Settings => Set<KastSettings>();
    public DbSet<ModPreset> ModPresets => Set<ModPreset>();
    public DbSet<ModPresetEntry> ModPresetEntries => Set<ModPresetEntry>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<MissionTag> MissionTags => Set<MissionTag>();
    public DbSet<MissionTagAssignment> MissionTagAssignments => Set<MissionTagAssignment>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignMission> CampaignMissions => Set<CampaignMission>();
    public DbSet<Set> Sets => Set<Set>();
    public DbSet<SetMission> SetMissions => Set<SetMission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerInstanceMod>(entity =>
        {
            entity.HasKey(e => new { e.ServerInstanceId, e.SteamModId });

            entity.HasOne(e => e.ServerInstance)
                .WithMany(s => s.Mods)
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SteamMod)
                .WithMany(m => m.ServerInstances)
                .HasForeignKey(e => e.SteamModId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HeadlessClient>(entity =>
        {
            entity.HasOne(e => e.ServerInstance)
                .WithMany(s => s.HeadlessClients)
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SteamMod>(entity =>
        {
            entity.HasIndex(e => e.WorkshopId).IsUnique();
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasIndex(e => e.KeyPrefix);
        });

        modelBuilder.Entity<KastUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedUsername).IsUnique();
            entity.Property(e => e.Username).HasMaxLength(64);
            entity.Property(e => e.NormalizedUsername).HasMaxLength(64);
            entity.Property(e => e.AvatarFileName).HasMaxLength(255);
        });

        // ── Mod Presets ────────────────────────────────────────────────────

        modelBuilder.Entity<ModPreset>(entity =>
        {
            entity.HasOne(e => e.ServerInstance)
                .WithMany()
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ServerInstanceId);
        });

        modelBuilder.Entity<ModPresetEntry>(entity =>
        {
            entity.HasKey(e => new { e.ModPresetId, e.SteamModId });

            entity.HasOne(e => e.ModPreset)
                .WithMany(p => p.Entries)
                .HasForeignKey(e => e.ModPresetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SteamMod)
                .WithMany()
                .HasForeignKey(e => e.SteamModId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Missions ────────────────────────────────────────────────────────

        modelBuilder.Entity<Mission>(entity =>
        {
            entity.HasOne(e => e.ServerInstance)
                .WithMany()
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ModPreset)
                .WithMany(p => p.Missions)
                .HasForeignKey(e => e.ModPresetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.ServerInstanceId);
            entity.HasIndex(e => new { e.ServerInstanceId, e.FileName }).IsUnique();
        });

        modelBuilder.Entity<MissionTag>(entity =>
        {
            entity.HasOne(e => e.ServerInstance)
                .WithMany()
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ServerInstanceId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<MissionTagAssignment>(entity =>
        {
            entity.HasKey(e => new { e.MissionId, e.MissionTagId });

            entity.HasOne(e => e.Mission)
                .WithMany(m => m.TagAssignments)
                .HasForeignKey(e => e.MissionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tag)
                .WithMany(t => t.MissionAssignments)
                .HasForeignKey(e => e.MissionTagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Campaigns ───────────────────────────────────────────────────────

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasOne(e => e.ServerInstance)
                .WithMany()
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ServerInstanceId);
        });

        modelBuilder.Entity<CampaignMission>(entity =>
        {
            entity.HasKey(e => new { e.CampaignId, e.MissionId });

            entity.HasOne(e => e.Campaign)
                .WithMany(c => c.CampaignMissions)
                .HasForeignKey(e => e.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Mission)
                .WithMany(m => m.CampaignMissions)
                .HasForeignKey(e => e.MissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Sets ────────────────────────────────────────────────────────────

        modelBuilder.Entity<Set>(entity =>
        {
            entity.HasOne(e => e.ServerInstance)
                .WithMany()
                .HasForeignKey(e => e.ServerInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ServerInstanceId);
        });

        modelBuilder.Entity<SetMission>(entity =>
        {
            entity.HasKey(e => new { e.SetId, e.MissionId });

            entity.HasOne(e => e.Set)
                .WithMany(s => s.SetMissions)
                .HasForeignKey(e => e.SetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Mission)
                .WithMany(m => m.SetMissions)
                .HasForeignKey(e => e.MissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
