// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.



using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KAST.Application.Common.Interfaces
{
    public interface IApplicationDbContext
    {
        DbSet<Logger> Loggers { get; set; }
        DbSet<AuditTrail> AuditTrails { get; set; }

        DbSet<Mod> Mods { get; set; }
        DbSet<Author> Authors { get; set; }

        DbSet<Document> Documents { get; set; }
        DbSet<KeyValue> KeyValues { get; set; }
        DbSet<Product> Products { get; set; }
        DbSet<Tenant> Tenants { get; set; }
        DbSet<Customer> Customers { get; set; }
        ChangeTracker ChangeTracker { get; }
        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    }
}