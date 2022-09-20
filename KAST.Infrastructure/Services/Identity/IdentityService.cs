// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Common.Interfaces.Identity.DTOs;
using KAST.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace KAST.Infrastructure.Services.Identity
{
    public class IdentityService : IIdentityService
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly IServiceProvider _serviceProvider;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly IOptions<AppConfigurationSettings> _appConfig;
        private readonly IUserClaimsPrincipalFactory<ApplicationUser> _userClaimsPrincipalFactory;
        private readonly IAuthorizationService _authorizationService;
        private readonly IStringLocalizer<IdentityService> _localizer;

        public IdentityService(
            IServiceProvider serviceProvider,
            IOptions<AppConfigurationSettings> appConfig,
            IUserClaimsPrincipalFactory<ApplicationUser> userClaimsPrincipalFactory,
            IAuthorizationService authorizationService,
            IStringLocalizer<IdentityService> localizer)
        {
            _serviceProvider = serviceProvider;
            _userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            _roleManager = _serviceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            _appConfig = appConfig;
            _userClaimsPrincipalFactory = userClaimsPrincipalFactory;
            _authorizationService = authorizationService;
            _localizer = localizer;
        }

        public async Task<string?> GetUserNameAsync(string userId)
        {
            await _semaphore.WaitAsync();
            try
            {
                var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);
                return user?.UserName;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<(Result Result, string UserId)> CreateUserAsync(string userName, string password)
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = userName,
            };

            var result = await _userManager.CreateAsync(user, password);

            return (result.ToApplicationResult(), user.Id);
        }

        public async Task<bool> IsInRoleAsync(string userId, string role)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is not null)
                return await _userManager.IsInRoleAsync(user, role);
            else
                return false;
        }

        public async Task<bool> AuthorizeAsync(string userId, string policyName)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is not null)
            {
                var principal = await _userClaimsPrincipalFactory.CreateAsync(user);
                var result = await _authorizationService.AuthorizeAsync(principal, policyName);
                return result.Succeeded;
            }
            else
            {
                return false;
            }
        }

        public async Task<Result> DeleteUserAsync(string userId)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);

            if (user != null)
            {
                return await DeleteUserAsync(user);
            }

            return await Result.SuccessAsync();
        }

        public async Task<Result> DeleteUserAsync(ApplicationUser user)
        {
            var result = await _userManager.DeleteAsync(user);

            return result.ToApplicationResult();
        }

        public async Task<IDictionary<string, string?>> FetchUsers(string roleName)
        {
            var result = await _userManager.Users
                 .Where(x => x.UserRoles.Where(y => y.Role.Name == roleName).Any())
                 .Include(x => x.UserRoles)
                 .ToDictionaryAsync(x => x.UserName, y => y.DisplayName);
            return result;
        }

        public async Task<Result<TokenResponse>> LoginAsync(TokenRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.UserName);
            if (user == null)
            {
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["User Not Found."] });
            }
            if (!user.IsActive)
            {
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["User Not Active. Please contact the administrator."] });
            }
            if (!user.EmailConfirmed)
            {
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["E-Mail not confirmed."] });
            }
            var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!passwordValid)
            {
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["Invalid Credentials."] });
            }

            user.RefreshToken = GenerateRefreshToken();
            var TokenExpiryTime = DateTime.Now.AddDays(7);

            if (request.RememberMe)
            {
                TokenExpiryTime = DateTime.Now.AddYears(1);
            }
            user.RefreshTokenExpiryTime = TokenExpiryTime;
            await _userManager.UpdateAsync(user);

            var token = await GenerateJwtAsync(user);
            var response = new TokenResponse { Token = token, RefreshTokenExpiryTime = TokenExpiryTime, RefreshToken = user.RefreshToken, ProfilePictureDataUrl = user.ProfilePictureDataUrl };
            return await Result<TokenResponse>.SuccessAsync(response);
        }

        public async Task<Result<TokenResponse>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            if (request is null)
            {
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["Invalid Client Token."] });
            }
            var userPrincipal = GetPrincipalFromExpiredToken(request.Token);
            var userEmail = userPrincipal.FindFirstValue(ClaimTypes.Email);
            var user = await _userManager.FindByEmailAsync(userEmail);
            if (user == null)
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["User Not Found."] });
            if (user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.Now)
                return await Result<TokenResponse>.FailureAsync(new string[] { _localizer["Invalid Client Token."] });
            var token = GenerateEncryptedToken(GetSigningCredentials(), await GetClaimsAsync(user));
            user.RefreshToken = GenerateRefreshToken();
            await _userManager.UpdateAsync(user);

            var response = new TokenResponse { Token = token, RefreshToken = user.RefreshToken, RefreshTokenExpiryTime = user.RefreshTokenExpiryTime };
            return await Result<TokenResponse>.SuccessAsync(response);
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
        private async Task<string> GenerateJwtAsync(ApplicationUser user)
        {
            var token = GenerateEncryptedToken(GetSigningCredentials(), await GetClaimsAsync(user));
            return token;
        }
        private async Task<IEnumerable<Claim>> GetClaimsAsync(ApplicationUser user)
        {
            var userClaims = await _userManager.GetClaimsAsync(user);
            var roles = await _userManager.GetRolesAsync(user);
            var roleClaims = new List<Claim>();
            var permissionClaims = new List<Claim>();
            foreach (var role in roles)
            {
                roleClaims.Add(new Claim(ClaimTypes.Role, role));
                var thisRole = await _roleManager.FindByNameAsync(role);
                var allPermissionsForThisRoles = await _roleManager.GetClaimsAsync(thisRole);
                permissionClaims.AddRange(allPermissionsForThisRoles);
            }

            var claims = new List<Claim>
            {
                new(ApplicationClaimTypes.Provider, user.Provider?? string.Empty),
                new(ApplicationClaimTypes.TenantId, user.TenantId?? string.Empty),
                new(ApplicationClaimTypes.TenantName, user.TenantName?? string.Empty),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ApplicationClaimTypes.ProfilePictureDataUrl, user.ProfilePictureDataUrl?? string.Empty),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.GivenName, user.DisplayName?? string.Empty),
                new(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty)
            }
            .Union(userClaims)
            .Union(roleClaims)
            .Union(permissionClaims);

            return claims;
        }
        private string GenerateEncryptedToken(SigningCredentials signingCredentials, IEnumerable<Claim> claims)
        {
            var token = new JwtSecurityToken(
               claims: claims,
               expires: DateTime.UtcNow.AddDays(2),
               signingCredentials: signingCredentials);
            var tokenHandler = new JwtSecurityTokenHandler();
            var encryptedToken = tokenHandler.WriteToken(token);
            return encryptedToken;
        }
        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_appConfig.Value.Secret)),
                ValidateIssuer = false,
                ValidateAudience = false,
                RoleClaimType = ClaimTypes.Role,
                ClockSkew = TimeSpan.Zero,
                ValidateLifetime = false
            };
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtSecurityToken || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256,
                StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException(_localizer["Invalid token"]);
            }

            return principal;
        }

        private SigningCredentials GetSigningCredentials()
        {
            var secret = Encoding.UTF8.GetBytes(_appConfig.Value.Secret);
            return new SigningCredentials(new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256);
        }

        public async Task UpdateLiveStatus(string userId, bool isLive)
        {
            await _semaphore.WaitAsync();
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user is not null && user.IsLive != isLive)
                {
                    user.IsLive = isLive;
                    await _userManager.UpdateAsync(user);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}