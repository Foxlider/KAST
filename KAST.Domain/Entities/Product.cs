// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Domain.Entities
{
    public class Product : AuditableEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? Unit { get; set; }
        public decimal Price { get; set; }
        public IList<string>? Pictures { get; set; }

    }
}