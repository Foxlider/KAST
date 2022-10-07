using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Application.Features.Mods.Caching
{
    public static class ModCacheKey
    {
        public const string GetAllCacheKey = "all-Mods";
        public static string GetPaginationCacheKey(string parameters)
        {
            return $"ModsWithPaginationQuery,{parameters}";
        }
        static ModCacheKey()
        {
            _tokensource = new CancellationTokenSource(new TimeSpan(1, 0, 0));
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
