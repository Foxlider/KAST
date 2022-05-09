using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Reflection;

namespace KAST.Core.Models
{
    public class KastContext : DbContext
    {
        public DbSet<Mod> Mods { get; set; }
        public DbSet<Author> Authors { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Settings> Settings { get; set; }

        public string DbPath { get; }

        public KastContext()
        {
            DbPath = Path.Join(Utilities.GetAppDataPath(), "Kast.db");

            if (!Directory.Exists(DbPath))
                Directory.CreateDirectory(Path.GetDirectoryName(DbPath));
        }

        // The following configures EF to create a Sqlite database file in the
        // special "local" folder for your platform.
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath}")
                      .UseLazyLoadingProxies();
    }
}
