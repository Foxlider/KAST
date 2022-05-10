using Microsoft.EntityFrameworkCore;

namespace KAST.Core.Models
{
    public class KastContext : DbContext
    {
        public DbSet<Mod> Mods { get; set; }
        public DbSet<Author> Authors { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Settings> Settings { get; set; }

        public string DbPath { get; private set; }


        public KastContext()
        { ChangeDbPath(Utilities.GetAppDataPath()); }

        /// <summary>
        /// Constructor with forced path
        /// </summary>
        /// <param name="path"></param>
        public KastContext(string forcedPath)
        { ChangeDbPath(forcedPath); }

        public void ChangeDbPath(string path)
        {
            DbPath = Path.Join(path, "KAST.db");

            if (!Directory.Exists(DbPath))
                Directory.CreateDirectory(Path.GetDirectoryName(DbPath));
        }

        // The following configures EF to create a Sqlite database file in the
        // special "local" folder for your platform.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite($"Data Source={DbPath}")
                      .UseLazyLoadingProxies();
    }
}
