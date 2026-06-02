using KAST.Core.Enums;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.Tests;

public class KastDbContextSqliteTests
{
    [Fact]
    public async Task SaveChangesAsync_WithConcurrentSqliteContexts_SerializesWrites()
    {
        var dbPath = Path.Join(Path.GetTempPath(), $"kast-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<KastDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .Options;

        try
        {
            await using (var setupDb = new KastDbContext(options))
                await setupDb.Database.EnsureCreatedAsync();

            var tasks = Enumerable.Range(1, 24)
                .Select(i => AddModAsync(options, i));

            await Task.WhenAll(tasks);

            await using var verifyDb = new KastDbContext(options);
            Assert.Equal(24, await verifyDb.Mods.CountAsync());
        }
        finally
        {
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-shm");
            DeleteIfExists($"{dbPath}-wal");
        }
    }

    private static async Task AddModAsync(DbContextOptions<KastDbContext> options, int index)
    {
        await using var db = new KastDbContext(options);
        db.Mods.Add(new SteamMod
        {
            Name = $"Concurrent Mod {index}",
            WorkshopId = 100000 + index,
            Source = ModSource.SteamWorkshop,
            Status = ModStatus.NotInstalled
        });

        await db.SaveChangesAsync();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
