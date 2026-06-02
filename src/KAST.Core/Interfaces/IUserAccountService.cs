using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IUserAccountService
{
    Task<bool> HasAnyUsersAsync(CancellationToken ct = default);
    Task<KastUser?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<KastUser?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<KastUser?> GetByExternalLoginAsync(string issuer, string subject, CancellationToken ct = default);
    Task<IReadOnlyList<KastUser>> GetAllUsersAsync(CancellationToken ct = default);
    Task<KastUser> CreateInitialAdminAsync(string username, string password, Stream? avatarStream = null, string? avatarFileName = null, long avatarLength = 0, CancellationToken ct = default);
    Task<KastUser> CreateAdminAsync(string username, string password, Stream? avatarStream = null, string? avatarFileName = null, long avatarLength = 0, CancellationToken ct = default);
    Task<KastUser> ProvisionOidcAdminAsync(OidcProvisioningRequest request, CancellationToken ct = default);
    Task<KastUser?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default);
    Task<KastUser> UpdateProfileAsync(int userId, string username, string? newPassword = null, Stream? avatarStream = null, string? avatarFileName = null, long avatarLength = 0, CancellationToken ct = default);
    Task<string?> GetAvatarPathAsync(string fileName, CancellationToken ct = default);
}
