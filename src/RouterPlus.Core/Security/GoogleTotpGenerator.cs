using System.Security.Cryptography;
using System.Text;

namespace RouterPlus.Core.Security;

/// <summary>
/// RFC 6238-compatible TOTP generator using HMAC-SHA1.
/// </summary>
public static class GoogleTotpGenerator
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Generate(
        string secret,
        DateTimeOffset utcNow,
        int digits = 6,
        int periodSeconds = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret, nameof(secret));

        if (digits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be positive.");
        }

        if (periodSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds), "Period must be positive.");
        }

        var keyBytes = DecodeBase32(secret);
        var counter = (ulong)(utcNow.ToUnixTimeSeconds() / periodSeconds);
        var counterBytes = BitConverter.GetBytes(counter);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, digits);
        return otp.ToString($"D{digits}");
    }

    private static byte[] DecodeBase32(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input, nameof(input));

        var cleanInput = input
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToUpperInvariant();

        if (cleanInput.Any(c => !Base32Alphabet.Contains(c)))
        {
            throw new FormatException("Invalid Base32 character in TOTP secret.");
        }

        var bits = new StringBuilder();
        foreach (var c in cleanInput)
        {
            var value = Base32Alphabet.IndexOf(c);
            bits.Append(Convert.ToString(value, 2).PadLeft(5, '0'));
        }

        var byteCount = bits.Length / 8;
        var result = new byte[byteCount];

        for (var i = 0; i < byteCount; i++)
        {
            var byteString = bits.ToString(i * 8, 8);
            result[i] = Convert.ToByte(byteString, 2);
        }

        return result;
    }
}
