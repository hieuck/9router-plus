namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Provider-neutral result for one OAuth consent attempt.
/// </summary>
public sealed record ProviderOAuthResult(
    bool Success,
    bool AlreadyAuthorized,
    string Message)
{
    public static ProviderOAuthResult FromConsent(OAuthConsentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProviderOAuthResult(result.Success, result.AlreadyAuthorized, result.Message);
    }
}
