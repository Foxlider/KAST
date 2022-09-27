// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Domain.Entities
{
    public class Mod : AuditableEntity
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }
        public virtual Author? Author { get; set; }
        public string? Path { get; set; }
        public DateTime? SteamLastUpdated{ get; set; }
        public DateTime? LocalLastUpdated { get; set; }

    }
}