using KAST.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace KAST.Data;

public class ApplicationDbContext : DbContext
{
    public string DbPath { get; }

    #region DbSets
    public DbSet<Server> Servers { get; set; }
    public DbSet<KastSettings> Settings { get; set; }
    public DbSet<Mod> Mods { get; set; }
    public DbSet<Profile> Profiles { get; set; }
    public DbSet<ModProfile> ModProfiles { get; set; }
    public DbSet<ProfileHistory> ProfileHistories { get; set; }
    #endregion


    protected readonly IHostEnvironment HostEnvironment;
    public static readonly ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { /*builder.AddDebug();*/ });

    public ApplicationDbContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Path.Combine(Environment.GetFolderPath(folder), "KAST");
        Directory.CreateDirectory(path);
        if(HostEnvironment != null && HostEnvironment.IsDevelopment())
            DbPath = Path.Join(path, "KAST-dev.db");
        else
            DbPath = Path.Join(path, "KAST.db");
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> dbContextOptions, IHostEnvironment env) : base(dbContextOptions)
    {
        HostEnvironment = env;
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Path.Combine(Environment.GetFolderPath(folder), "KAST");
        Directory.CreateDirectory(path);
        if (HostEnvironment.IsDevelopment())
            DbPath = Path.Join(path, "KAST-dev.db");
        else
            DbPath = Path.Join(path, "KAST.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder
            .UseLoggerFactory(loggerFactory)
            .UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Mod entity
        modelBuilder.Entity<Mod>(entity =>
        {
            entity.HasIndex(e => e.SteamId).IsUnique();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Path).IsRequired();
        });

        // Configure Profile entity
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.HasOne(e => e.Server)
                  .WithMany(s => s.Profiles)
                  .HasForeignKey(e => e.ServerId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure ModProfile many-to-many relationship
        modelBuilder.Entity<ModProfile>(entity =>
        {
            entity.HasOne(mp => mp.Mod)
                  .WithMany(m => m.ModProfiles)
                  .HasForeignKey(mp => mp.ModId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(mp => mp.Profile)
                  .WithMany(p => p.ModProfiles)
                  .HasForeignKey(mp => mp.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique combination of mod and profile
            entity.HasIndex(mp => new { mp.ModId, mp.ProfileId }).IsUnique();
        });

        // Configure ProfileHistory
        modelBuilder.Entity<ProfileHistory>(entity =>
        {
            entity.HasOne(ph => ph.Profile)
                  .WithMany(p => p.ProfileHistories)
                  .HasForeignKey(ph => ph.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique combination of profile and version
            entity.HasIndex(ph => new { ph.ProfileId, ph.Version }).IsUnique();
        });

        // Configure Server entity
        modelBuilder.Entity<Server>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.InstallPath).IsRequired();
        });
    }

    public void EnsureSeedData()
    { /* So far we have no need to seed the DB as the models are not ready for production yet */ }

}
