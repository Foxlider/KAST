using System.Security.Cryptography;

namespace KAST.UI.Services;

public sealed class InternalHubTokenService
{
    private readonly string _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string Token => _token;

    public bool IsValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var expected = Convert.FromBase64String(_token);
        byte[] actual;
        try
        {
            actual = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            return false;
        }

        return actual.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
