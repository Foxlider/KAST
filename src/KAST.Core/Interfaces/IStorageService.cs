using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IStorageService
{
    Task<StorageScanResult> ScanStorageAsync(StorageScanRequest request, CancellationToken ct = default);
    Task<StorageApplyResult> ApplyStorageChangesAsync(StorageApplyRequest request, CancellationToken ct = default);
    Task<StorageMigrationResult> MigrateStorageAsync(StorageMigrationRequest request, CancellationToken ct = default);
}
