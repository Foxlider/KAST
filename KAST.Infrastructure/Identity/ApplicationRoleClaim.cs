// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Infrastructure.Identity
{
    public class ApplicationRoleClaim : IdentityRoleClaim<string>
    {
        public string? Description { get; set; }
        public string? Group { get; set; }
        public virtual ApplicationRole Role { get; set; } = default!;


    }
}