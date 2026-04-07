using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace KAST.Infrastructure.Data;

public class KastDbContext : DbContext
{
    public KastDbContext(DbContextOptions<KastDbContext> options) : base(options) { }

    public DbSet<SteamMod> Mods => Set<SteamMod>();
    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
    public DbSet<ServerInstanceMod> ServerInstanceMods => Set<ServerInstanceMod>();
    public DbSet<HeadlessClient> HeadlessClients => Set<HeadlessClient>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<DownloadTask> DownloadTasks => Set<DownloadTask>();

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
    }
}
