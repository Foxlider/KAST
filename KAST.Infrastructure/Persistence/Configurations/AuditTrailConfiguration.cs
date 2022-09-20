// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Common.Interfaces.Serialization;
using KAST.Infrastructure.Services.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace KAST.Infrastructure.Persistence.Configurations
{
#nullable disable
    public class AuditTrailConfiguration : IEntityTypeConfiguration<AuditTrail>
    {
        public void Configure(EntityTypeBuilder<AuditTrail> builder)
        {

            builder.Property(t => t.AuditType)
               .HasConversion<string>();
            builder.Property(e => e.AffectedColumns)
               .HasConversion(
                     v => JsonSerializer.Serialize(v, DefaultJsonSerializerOptions.Options),
                     v => JsonSerializer.Deserialize<List<string>>(v, DefaultJsonSerializerOptions.Options),
                     new ValueComparer<ICollection<string>>(
                            (c1, c2) => c1.SequenceEqual(c2),
                                   c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                                   c => (ICollection<string>)c.ToList()));

            builder.Property(u => u.OldValues)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, DefaultJsonSerializerOptions.Options),
                    s => JsonSerializer.Deserialize<Dictionary<string, object>>(s, DefaultJsonSerializerOptions.Options)
                );
            builder.Property(u => u.NewValues)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<Dictionary<string, object>>(s, DefaultJsonSerializerOptions.Options)
                );
            builder.Property(u => u.PrimaryKey)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<Dictionary<string, object>>(s, DefaultJsonSerializerOptions.Options)
                );

            builder.Ignore(x => x.TemporaryProperties);
            builder.Ignore(x => x.HasTemporaryProperties);
        }
    }
}