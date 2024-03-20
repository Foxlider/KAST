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
    public DbSet<Settings> Settings { get; set; }
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
    { }

    public void EnsureSeedData()
    { }

}
