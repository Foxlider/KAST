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
            ["ConnectionStrings:Default"] = $"Data Source={dbPath ?? "kast.db"}"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
