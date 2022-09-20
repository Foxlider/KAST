
using KAST.Application.Common.Interfaces.MultiTenant;
using KAST.Infrastructure.Services.MultiTenant;

namespace KAST.Infrastructure.Extensions
{
    public static class MultiTenantServiceCollectionExtensions
    {
        public static IServiceCollection AddMultiTenantService(this IServiceCollection services)
            => services.AddScoped<ITenantProvider, TenantProvider>();
    }
}