// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Features.Documents.Caching
{
    public static class DocumentCacheKey
    {
        public const string GetAllCacheKey = "all-documents";
        public static string GetStreamByIdKey(int id) => $"GetStreamByIdKey:{id}";
        static DocumentCacheKey()
        {
            _tokensource = new CancellationTokenSource(new TimeSpan(12, 0, 0));
        }
        private static CancellationTokenSource _tokensource;
        public static CancellationTokenSource SharedExpiryTokenSource()
        {
            if (_tokensource.IsCancellationRequested)
            {
                _tokensource = new CancellationTokenSource(new TimeSpan(3, 0, 0));
            }
            return _tokensource;
        }
        public static MemoryCacheEntryOptions MemoryCacheEntryOptions => new MemoryCacheEntryOptions().AddExpirationToken(new CancellationChangeToken(SharedExpiryTokenSource().Token));
    }
}