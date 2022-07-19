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
        /// Constructor with forced options
        /// </summary>
        /// <param name="options"></param>
        public KastContext(DbContextOptions<KastContext> options) : base(options)
        { }

        public void ChangeDbPath(string path)
        {
            DbPath = Path.Join(path, "KAST.db");

            if (!Directory.Exists(DbPath))
                Directory.CreateDirectory(Path.GetDirectoryName(DbPath) ?? throw new InvalidOperationException());
        }

        // The following configures EF to create a Sqlite database file in the
        // special "local" folder for your platform.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured)
                return;
            optionsBuilder.UseSqlite($"Data Source={DbPath}")
                      .UseLazyLoadingProxies();
        }
    }
}
