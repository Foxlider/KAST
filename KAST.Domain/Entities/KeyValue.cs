// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Domain.Entities
{
    public class KeyValue : AuditableEntity, IAuditTrial
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Value { get; set; }
        public string? Text { get; set; }
        public string? Description { get; set; }

    }
}