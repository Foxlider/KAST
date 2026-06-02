namespace KAST.UI.Services;

public enum KastAuthMode
{
    Local,
    Oidc,
    LocalAndOidc
}

public sealed record KastAuthSettings(
    KastAuthMode Mode,
    string DisplayName,
    string? Authority,
    string? ClientId,
    string? ClientSecret,
    string GroupClaim,
    string NameClaim,
    IReadOnlyList<string> AllowedGroups)
{
    public bool IsLocalEnabled => Mode is KastAuthMode.Local or KastAuthMode.LocalAndOidc;
    public bool IsOidcRequested => Mode is KastAuthMode.Oidc or KastAuthMode.LocalAndOidc;
    public bool IsOidcConfigured =>
        IsOidcRequested &&
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}

public static class KastAuthSettingsExtensions
{
    public const string DefaultOidcDisplayName = "OpenID Connect";
    public const string DefaultOidcAllowedGroup = "KAST Admins";
    public const string DefaultOidcGroupClaim = "groups";
    public const string DefaultOidcNameClaim = "preferred_username";

    public static KastAuthSettings GetKastAuthSettings(this IConfiguration configuration)
    {
        var mode = ParseMode(configuration["Auth:Mode"]);
        var allowedGroups = ReadList(configuration, "Auth:Oidc:AllowedGroups", DefaultOidcAllowedGroup);

        return new KastAuthSettings(
            mode,
            FirstNonBlank(configuration["Auth:Oidc:DisplayName"], DefaultOidcDisplayName),
            configuration["Auth:Oidc:Authority"],
            configuration["Auth:Oidc:ClientId"],
            configuration["Auth:Oidc:ClientSecret"],
            FirstNonBlank(configuration["Auth:Oidc:GroupClaim"], DefaultOidcGroupClaim),
            FirstNonBlank(configuration["Auth:Oidc:NameClaim"], DefaultOidcNameClaim),
            allowedGroups);
    }

    private static KastAuthMode ParseMode(string? value)
        => Enum.TryParse<KastAuthMode>(value, ignoreCase: true, out var mode)
            ? mode
            : KastAuthMode.Local;

    private static IReadOnlyList<string> ReadList(IConfiguration configuration, string key, string fallback)
    {
        var values = configuration.GetSection(key)
            .GetChildren()
            .Select(child => child.Value)
            .Concat(SplitConfigList(configuration[key]))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? [fallback] : values;
    }

    private static IEnumerable<string> SplitConfigList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
