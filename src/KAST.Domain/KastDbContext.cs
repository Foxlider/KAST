using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KAST.Data
{
    public class KastDbContext : DbContext
    {
        public DbSet<Mod> Mods { get; set; }
        public DbSet<SteamMod> SteamMods { get; set; }
        public DbSet<LocalMod> LocalMods { get; set; }
        public DbSet<Author> Authors { get; set; }

        public static readonly ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { builder.AddDebug(); });

        public string DbPath { get; }


        public KastDbContext(DbContextOptions<KastDbContext> dbContextOptions) : base(dbContextOptions)
        { }

        public KastDbContext()
        { }

        // The following configures EF to create a Sqlite database file in the
        // special "local" folder for your platform.
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            base.OnConfiguring(options);
            options.UseLoggerFactory(loggerFactory);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Mod>()
                .HasDiscriminator<bool>("IsLocal")
                .HasValue<LocalMod>(true)
                .HasValue<SteamMod>(false);
        }

        public void EnsureSeedData()
        {
            try
            {
                if(!Authors.Any())
                {
                    Authors.Add(new Author { Name = "Local" });
                    SaveChanges();
                }
            }
            catch (Exception) { throw; }
        }


        public void DebugSeedData()
        {
            try
            {
                var a1 = Authors.Add(new Author { Name = "acemod" });
                var a2 = Authors.Add(new Author { Name = "CBATeam" });
                SaveChanges();

                Mods.Add(new SteamMod { Author = a1.Entity, Name = "ace", Url = "https://steamcommunity.com/workshop/filedetails/?id=463939057"  });
                SaveChanges();
                Mods.Add(new SteamMod { Author = a2.Entity, Name = "CBA_A3", Url = "https://steamcommunity.com/workshop/filedetails/?id=450814997" });
                SaveChanges();
                Mods.Add(new LocalMod { Name = "AUX Mod", Path = "E:\\Mods\\AUX Mod" });
                SaveChanges();

            }
            catch (Exception) { throw;  }
        }
    }
}