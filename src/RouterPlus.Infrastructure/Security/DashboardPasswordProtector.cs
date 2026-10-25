using System.Security.Cryptography;
using System.Text;

namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Protects the 9Router dashboard password at rest using Windows DPAPI (CurrentUser scope).
/// Stored form is prefixed with <see cref="Prefix"/> so legacy plaintext values remain detectable.
/// </summary>
public sealed class DashboardPasswordProtector
{
    public const string Prefix = "dpapi:v1:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("9RouterPlus.DashboardAuth.v1");

    public DashboardPasswordProtector()
    {
    }

    public static bool IsProtected(string? value) =>
        !string.IsNullOrEmpty(value) && value!.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static bool TryUnprotect(string? protectedValue, out string? plaintext)
    {
        plaintext = null;

        if (string.IsNullOrWhiteSpace(protectedValue) || !IsProtected(protectedValue))
        {
            return false;
        }

        var payload = protectedValue!.Substring(Prefix.Length);
        try
        {
            var protectedBytes = Convert.FromBase64String(payload);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch
        {
            // Do not leak secret material or exception details.
            plaintext = null;
            return false;
        }
    }
}