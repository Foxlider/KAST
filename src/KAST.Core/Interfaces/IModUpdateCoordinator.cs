namespace KAST.Core.Interfaces;

/// <summary>
/// Coordinates bounded, independently cancellable bulk mod updates.
/// </summary>
public interface IModUpdateCoordinator
{
    Task UpdateAllOutdatedModsAsync(CancellationToken ct = default);
}