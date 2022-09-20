using KAST.Application.Features.Tenants.DTOs;

namespace KAST.Application.Common.Interfaces.MultiTenant
{
    public interface ITenantsService
    {
        List<TenantDto> DataSource { get; }
        event Action? OnChange;
        Task Initialize();
        Task Refresh();
    }
}