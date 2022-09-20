// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Common.Interfaces.Caching
{
    public interface ICacheable
    {
        string CacheKey { get => String.Empty; }
        MemoryCacheEntryOptions? Options { get; }
    }
}