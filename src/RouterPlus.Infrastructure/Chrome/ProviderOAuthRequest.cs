using RouterPlus.Core.Providers;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Inputs required by a provider OAuth adapter. Credentials remain owned by the
/// Google authentication service and are intentionally absent from this request.
/// </summary>
public sealed record ProviderOAuthRequest
{
    public ProviderOAuthRequest(
        ProviderKind provider,
        Uri authUri,
        Uri targetServiceUri,
        string profileEmail,
        TimeSpan timeout,
        CdpSession cdpSession,
        Func<Task<string?>>? totpGenerator = null)
    {
        Provider = provider;
        AuthUri = authUri ?? throw new ArgumentNullException(nameof(authUri));
        TargetServiceUri = targetServiceUri ?? throw new ArgumentNullException(nameof(targetServiceUri));
        ProfileEmail = string.IsNullOrWhiteSpace(profileEmail)
            ? throw new ArgumentException("Profile email is required.", nameof(profileEmail))
            : profileEmail;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "OAuth timeout must be positive.");
        }

        Timeout = timeout;
        CdpSession = cdpSession ?? throw new ArgumentNullException(nameof(cdpSession));
        TotpGenerator = totpGenerator;
    }

    public ProviderKind Provider { get; }
    public Uri AuthUri { get; }
    public Uri TargetServiceUri { get; }
    public string ProfileEmail { get; }
    public TimeSpan Timeout { get; }
    public CdpSession CdpSession { get; }
    public Func<Task<string?>>? TotpGenerator { get; }
}
