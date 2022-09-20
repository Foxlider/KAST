// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Domain.Entities.Log
{
    public class Logger : IEntity
    {
        public int Id { get; set; }
        public string? Message { get; set; }
        public string? MessageTemplate { get; set; }
        public string Level { get; set; } = default!;

        public DateTime TimeStamp { get; set; } = DateTime.Now;
        public string? Exception { get; set; }
        public string? UserName { get; set; }
        public string? ClientIP { get; set; }
        public string? ClientAgent { get; set; }
        public string? Properties { get; set; }
        public string? LogEvent { get; set; }

    }
}