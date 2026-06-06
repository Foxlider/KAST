using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IHostServiceManager
{
    Task<HostServiceStatus> GetStatusAsync(CancellationToken ct = default);
    Task<HostServiceOperationResult> InstallAsync(HostServiceConfigureRequest request, CancellationToken ct = default);
    Task<HostServiceOperationResult> ConfigureAsync(HostServiceConfigureRequest request, CancellationToken ct = default);
    Task<HostServiceOperationResult> UninstallAsync(CancellationToken ct = default);
    Task<HostServiceOperationResult> StartAsync(CancellationToken ct = default);
    Task<HostServiceOperationResult> StopAsync(CancellationToken ct = default);
}
