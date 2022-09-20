using KAST.Application.Common.Interfaces.MultiTenant;
using KAST.Application.Common.Security;
using KAST.Infrastructure.Constants.LocalStorage;
using KAST.Infrastructure.Constants.Role;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Text;

namespace KAST.Infrastructure.Services.Authentication
{
    public class IdentityAuthenticationService : AuthenticationStateProvider, IAuthenticationService
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly ITenantProvider _tenantProvider;
        private readonly ICurrentUserService _currentUserService;
        private readonly ProtectedLocalStorage _protectedLocalStorage;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private const string KEY = "Basic";

        public IdentityAuthenticationService(
        ITenantProvider tenantProvider,
        ICurrentUserService currentUserService,
        ProtectedLocalStorage protectedLocalStorage,
            RoleManager<ApplicationRole> roleManager,
            UserManager<ApplicationUser> userManager
            )
        {
            _tenantProvider = tenantProvider;
            _currentUserService = currentUserService;
            _protectedLocalStorage = protectedLocalStorage;
            _userManager = userManager;
            _roleManager = roleManager;
        }
        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity());
            try
            {
                var storedClaimsIdentity = await _protectedLocalStorage.GetAsync<string>(LocalStorage.CLAIMSIDENTITY);
                if (storedClaimsIdentity.Success && storedClaimsIdentity.Value is not null)
                {
                    var buffer = Convert.FromBase64String(storedClaimsIdentity.Value);
                    using (var deserializationStream = new MemoryStream(buffer))
                    {
                        var identity = new ClaimsIdentity(new BinaryReader(deserializationStream, Encoding.UTF8));
                        principal = new(identity);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return new AuthenticationState(principal);
        }

        private async Task<ClaimsIdentity> CreateIdentityFromApplicationUser(ApplicationUser user)
        {

            var result = new ClaimsIdentity(KEY);
            result.AddClaim(new(ClaimTypes.NameIdentifier, user.Id));
            result.AddClaim(new(ApplicationClaimTypes.Status, user.IsActive.ToString()));
            if (!string.IsNullOrEmpty(user.UserName))
            {
                result.AddClaims(new[] {
                new Claim(ClaimTypes.Name, user.UserName)
            });
            }
            if (!string.IsNullOrEmpty(user.TenantName))
            {
                result.AddClaims(new[] {
                new Claim(ApplicationClaimTypes.TenantName, user.TenantName)
            });
            }
            if (!string.IsNullOrEmpty(user.TenantId))
            {
                result.AddClaims(new[] {
                new Claim(ApplicationClaimTypes.TenantId, user.TenantId)
            });
            }

            if (!string.IsNullOrEmpty(user.Provider))
            {
                result.AddClaims(new[] {
                new Claim(ApplicationClaimTypes.Provider, user.Provider)
            });
            }
            if (!string.IsNullOrEmpty(user.Email))
            {
                result.AddClaims(new[] {
                new Claim(ClaimTypes.Email, user.Email)
            });
            }
            if (!string.IsNullOrEmpty(user.ProfilePictureDataUrl))
            {
                result.AddClaims(new[] {
                new Claim(ApplicationClaimTypes.ProfilePictureDataUrl, user.ProfilePictureDataUrl)
            });
            }
            if (!string.IsNullOrEmpty(user.DisplayName))
            {
                result.AddClaims(new[] {
                new Claim(ClaimTypes.GivenName, user.DisplayName)
            });
            }
            if (!string.IsNullOrEmpty(user.PhoneNumber))
            {
                result.AddClaims(new[] {
                new Claim(ClaimTypes.MobilePhone, user.PhoneNumber)
            });
            }
            var roles = await _userManager.GetRolesAsync(user);
            foreach (var roleName in roles)
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                var claims = await _roleManager.GetClaimsAsync(role);
                foreach (var claim in claims)
                {
                    result.AddClaim(claim);
                }
                result.AddClaims(new[] {
                new Claim(ClaimTypes.Role, roleName) });

            }
            return result;
        }


        public async Task<bool> Login(LoginFormModel request)
        {
            await _semaphore.WaitAsync();
            try
            {
                var user = await _userManager.FindByNameAsync(request.UserName);
                var valid = user.IsActive && await _userManager.CheckPasswordAsync(user, request.Password);
                if (valid)
                {

                    var identity = await CreateIdentityFromApplicationUser(user);
                    using (var memoryStream = new MemoryStream())
                    await using (var binaryWriter = new BinaryWriter(memoryStream, Encoding.UTF8, true))
                    {
                        identity.WriteTo(binaryWriter);
                        var base64 = Convert.ToBase64String(memoryStream.ToArray());
                        await _protectedLocalStorage.SetAsync(LocalStorage.CLAIMSIDENTITY, base64);
                    }
                    await _currentUserService.SetUser(user.Id, user.UserName);
                    if (user.TenantId is not null && user.TenantName is not null)
                    {
                        await _tenantProvider.SetTenant(user.TenantId, user.TenantName);
                    }
                    var principal = new ClaimsPrincipal(identity);
                    NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
                }
                return valid;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        public async Task<bool> ExternalLogin(string provider, string userName, string name, string accessToken)
        {
            await _semaphore.WaitAsync();
            try
            {
                var user = await _userManager.FindByNameAsync(userName);
                if (user is null)
                {
                    user = new ApplicationUser
                    {
                        EmailConfirmed = true,
                        IsActive = true,
                        IsLive = true,
                        UserName = userName,
                        Email = userName.Any(x => x == '@') ? userName : $"{userName}@{provider}.com",
                        Provider = provider,
                        DisplayName = name,
                    };
                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                    {
                        return false;
                    }
                    await _userManager.AddToRoleAsync(user, RoleConstants.BasicRole);
                    await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, userName, accessToken));
                }

                if (!user.IsActive)
                    return false;

                var identity = await CreateIdentityFromApplicationUser(user);
                using (var memoryStream = new MemoryStream())
                await using (var binaryWriter = new BinaryWriter(memoryStream, Encoding.UTF8, true))
                {
                    identity.WriteTo(binaryWriter);
                    var base64 = Convert.ToBase64String(memoryStream.ToArray());
                    await _protectedLocalStorage.SetAsync(LocalStorage.CLAIMSIDENTITY, base64);
                }
                await _currentUserService.SetUser(user.Id, user.UserName);
                if (user.TenantId is not null && user.TenantName is not null)
                {
                    await _tenantProvider.SetTenant(user.TenantId, user.TenantName);
                }
                var principal = new ClaimsPrincipal(identity);
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
                return true;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        public async Task Logout()
        {
            await _protectedLocalStorage.DeleteAsync(LocalStorage.CLAIMSIDENTITY);
            await _currentUserService.Clear();
            await _tenantProvider.Clear();
            var principal = new ClaimsPrincipal();
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
        }
    }
}