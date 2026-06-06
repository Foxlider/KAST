using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IServerInstanceService
{
    Task<IReadOnlyList<ServerInstance>> GetAllInstancesAsync(CancellationToken ct = default);
    Task<ServerInstance?> GetInstanceByIdAsync(int id, CancellationToken ct = default);
    Task<ServerInstance> CreateInstanceAsync(ServerInstance instance, CancellationToken ct = default);
    Task<ServerInstance> UpdateInstanceAsync(ServerInstance instance, CancellationToken ct = default);
    Task DeleteInstanceAsync(int id, bool deleteFiles = false, CancellationToken ct = default);

    Task StartInstanceAsync(int id, CancellationToken ct = default);
    Task StopInstanceAsync(int id, CancellationToken ct = default);
    Task RestartInstanceAsync(int id, CancellationToken ct = default);

    Task AddModToInstanceAsync(int instanceId, int modId, int loadOrder = 0, bool isClientSide = false, bool isServerSide = false, CancellationToken ct = default);
    Task RemoveModFromInstanceAsync(int instanceId, int modId, CancellationToken ct = default);
    Task UpdateModLoadOrderAsync(int instanceId, int modId, int newOrder, CancellationToken ct = default);
    Task UpdateModFlagsAsync(int instanceId, int modId, bool isClientSide, bool isServerSide, CancellationToken ct = default);

    Task LinkModsAsync(int instanceId, CancellationToken ct = default);

    /// <summary>Writes server.cfg, basic.cfg and Arma3Profile to the instance config directory.</summary>
    void WriteConfigFiles(ServerInstance instance);

    /// <summary>Returns the full command line (executable + args) for previewing.</summary>
    string GetCommandLine(ServerInstance instance);
}
