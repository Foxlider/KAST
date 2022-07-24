using KAST.Core.Models;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using System;
using System.Data.Common;

namespace KAST.Tests
{
    public class KastContextFactory : IDisposable
    {
        private DbConnection _connection;

        private DbContextOptions<KastContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<KastContext>()
                .UseSqlite(_connection).Options;
        }

        public KastContext CreateContext()
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (_connection != null)
                return new KastContext(CreateOptions());

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var       options = CreateOptions();
            using var context = new KastContext(options);
            context.Database.EnsureCreated();
            context.Add(new Mod(463939058) { Name = "TestName", Url = "TestUrl" });
            context.SaveChanges();

            return new KastContext(CreateOptions());
        }

        public void Dispose()
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
