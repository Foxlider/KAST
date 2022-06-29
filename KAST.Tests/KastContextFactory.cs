using KAST.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            if (_connection == null)
            {
                _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();

                var options = CreateOptions();
                using (var context = new KastContext(options))
                {
                    context.Database.EnsureCreated();
                    context.Add(new Mod(463939058) { Name = "TestName", Url = "TestUrl" });
                    context.SaveChanges();
                }
            }

            return new KastContext(CreateOptions());
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
            }
        }
    }
}
