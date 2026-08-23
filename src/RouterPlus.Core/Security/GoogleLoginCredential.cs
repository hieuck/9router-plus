using System.Text.RegularExpressions;

namespace RouterPlus.Core.Security;

/// <summary>
/// Immutable profile-bound Google login credential record.
/// </summary>
public sealed record GoogleLoginCredential
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public GoogleLoginCredential(string profileId, string email, string password, string totpSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId, nameof(profileId));
        ArgumentException.ThrowIfNullOrWhiteSpace(email, nameof(email));
        ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(password));
        ArgumentException.ThrowIfNullOrWhiteSpace(totpSecret, nameof(totpSecret));

        var trimmedProfileId = profileId.Trim();
        var trimmedEmail = email.Trim();

        if (string.IsNullOrWhiteSpace(trimmedProfileId))
        {
            throw new ArgumentException("Profile ID cannot be blank after trimming.", nameof(profileId));
        }

        if (!EmailRegex.IsMatch(trimmedEmail))
        {
            throw new FormatException($"Invalid email format: {trimmedEmail}");
        }

        ProfileId = trimmedProfileId;
        Email = trimmedEmail;
        Password = password;
        TotpSecret = totpSecret;
    }

    public string ProfileId { get; }
    public string Email { get; }
    public string Password { get; }
    public string TotpSecret { get; }
}
