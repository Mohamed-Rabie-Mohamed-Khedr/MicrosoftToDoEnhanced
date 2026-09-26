using System.Security.Cryptography;
using System.Text;

namespace Core.Security;

public static class PasswordHasher
{
    public const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string FormatPrefix = "pbkdf2";

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{FormatPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool IsLegacySha256(string stored)
    {
        if (string.IsNullOrEmpty(stored) || stored.Length != 64)
            return false;

        foreach (char c in stored)
            if (!Uri.IsHexDigit(c))
                return false;

        return true;
    }

    public static VerifyResult Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return new VerifyResult(false, false);

        if (IsLegacySha256(stored))
        {
            bool valid = VerifyLegacySha256(password, stored);
            return new VerifyResult(valid, valid);
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != FormatPrefix)
            return new VerifyResult(false, false);

        if (!int.TryParse(parts[1], out int iterations) || iterations < 1)
            return new VerifyResult(false, false);

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return new VerifyResult(false, false);
        }

        if (salt.Length == 0 || expected.Length == 0)
            return new VerifyResult(false, false);

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        bool matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        return new VerifyResult(matches, false);
    }

    private static bool VerifyLegacySha256(string password, string stored)
    {
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        string actualHex = Convert.ToHexString(actual);
        return string.Equals(actualHex, stored, StringComparison.OrdinalIgnoreCase);
    }

    public readonly record struct VerifyResult(bool Success, bool NeedsRehash);
}
