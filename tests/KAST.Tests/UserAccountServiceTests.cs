using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace KAST.Tests;

public class UserAccountServiceTests
{
    [Fact]
    public async Task CreateInitialAdmin_PersistsUserAndHash()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());

        var user = await sut.CreateInitialAdminAsync("admin", "secret");

        Assert.True(user.Id > 0);
        Assert.Equal("admin", user.Username);
        Assert.Equal("ADMIN", user.NormalizedUsername);
        Assert.DoesNotContain("secret", user.PasswordHash);
        Assert.True(await sut.HasAnyUsersAsync());
    }

    [Fact]
    public async Task CreateAdmin_DuplicateUsername_Throws()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());

        await sut.CreateInitialAdminAsync("admin", "secret");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CreateAdminAsync("ADMIN", "other-secret"));
    }

    [Fact]
    public async Task ValidateCredentials_UpdatesLastLogin()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());
        await sut.CreateInitialAdminAsync("admin", "secret");

        var invalid = await sut.ValidateCredentialsAsync("admin", "wrong");
        var valid = await sut.ValidateCredentialsAsync("admin", "secret");

        Assert.Null(invalid);
        Assert.NotNull(valid);
        Assert.NotNull(valid!.LastLoginAt);
    }

    [Fact]
    public async Task ProvisionOidcAdmin_CreatesFirstAdminFromAllowedGroup()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());

        var user = await sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test/application/o/kast/",
            "user-123",
            "Authentik Admin",
            "admin@example.test",
            ["KAST Admins"]));

        Assert.True(user.Id > 0);
        Assert.Equal("Authentik Admin", user.Username);
        Assert.Equal(KastUser.OidcAuthSource, user.AuthSource);
        Assert.Equal("OpenID Connect", user.ExternalProvider);
        Assert.Equal("https://auth.example.test/application/o/kast/", user.ExternalIssuer);
        Assert.Equal("user-123", user.ExternalSubject);
        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public async Task ProvisionOidcAdmin_CreatesLaterAdminWhenAllowedGroupIsPresent()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());
        await sut.CreateInitialAdminAsync("local-admin", "secret");

        var user = await sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test",
            "user-456",
            "External Admin",
            null,
            ["KAST Admins"]));

        Assert.Equal(KastUser.OidcAuthSource, user.AuthSource);
        Assert.Equal(2, db.Users.Count());
    }

    [Fact]
    public async Task ProvisionOidcAdmin_RejectsMissingIdentityOrAllowedGroup()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            null,
            "user-123",
            "Admin",
            null,
            ["KAST Admins"])));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test",
            null,
            "Admin",
            null,
            ["KAST Admins"])));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test",
            "user-123",
            "Admin",
            null,
            ["Other Group"])));
    }

    [Fact]
    public async Task ProvisionOidcAdmin_ReusesExistingExternalUserAndSyncsUsername()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());

        var first = await sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test",
            "user-123",
            "Old Name",
            null,
            ["KAST Admins"]));
        var second = await sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test",
            "user-123",
            "New Name",
            null,
            ["KAST Admins"]));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("New Name", second.Username);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task ValidateCredentials_DoesNotAuthenticateOidcUsers()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());

        await sut.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
            "https://auth.example.test",
            "user-123",
            "External Admin",
            null,
            ["KAST Admins"]));

        Assert.Null(await sut.ValidateCredentialsAsync("External Admin", "anything"));
    }

    [Fact]
    public async Task UpdateProfile_ChangesUsernameAndPassword()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var sut = new UserAccountService(db, BuildConfig());
        var user = await sut.CreateInitialAdminAsync("admin", "secret");

        var updated = await sut.UpdateProfileAsync(user.Id, "root", "new-secret");

        Assert.Equal("root", updated.Username);
        Assert.Null(await sut.ValidateCredentialsAsync("root", "secret"));
        Assert.NotNull(await sut.ValidateCredentialsAsync("root", "new-secret"));
    }

    [Fact]
    public async Task AvatarUpload_StoresAllowedImageAndRejectsInvalidExtension()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"kast-user-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using var db = DbHelper.CreateInMemoryDb();
            var sut = new UserAccountService(db, BuildConfig(Path.Combine(tempRoot, "kast.db")));

            await using var image = new MemoryStream([1, 2, 3]);
            var user = await sut.CreateInitialAdminAsync("admin", "secret", image, "avatar.png", image.Length);

            Assert.False(string.IsNullOrWhiteSpace(user.AvatarFileName));
            var avatarPath = await sut.GetAvatarPathAsync(user.AvatarFileName!);
            Assert.True(File.Exists(avatarPath));

            await using var invalid = new MemoryStream([1, 2, 3]);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.CreateAdminAsync("other", "secret", invalid, "avatar.gif", invalid.Length));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static IConfiguration BuildConfig(string? dbPath = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={dbPath ?? "kast.db"}",
            ["Auth:Oidc:AllowedGroups:0"] = "KAST Admins"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
