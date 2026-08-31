namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Result of OAuth consent flow.
/// </summary>
public sealed record OAuthConsentResult(
    bool Success,
    bool AlreadyAuthorized,
    string Message);