using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.Tests.Helpers;

public static class DbHelper
{
    public static KastDbContext CreateInMemoryDb(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<KastDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var db = new KastDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
