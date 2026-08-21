namespace RouterPlus.Infrastructure.Router;

public sealed record OAuthAuthorizationSession(
    string AuthUrl,
    string State,
    string CodeVerifier,
    string RedirectUri,
    string FlowType,
    bool FixedPort,
    string? CallbackPath);

public sealed record OAuthProxyStartResult(bool Success, bool ServerSide);

public sealed record OAuthProxyStatus(string Status, string? Error);

public sealed record DeviceCodeSession(
    string DeviceCode,
    string? UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int ExpiresIn,
    int Interval,
    string? ClientId,
    string? ClientSecret,
    string? Region,
    string? AuthMethod,
    string? StartUrl,
    string? CodeVerifier);

public sealed record DeviceCodePollResult(bool Success, string? Error, string? ErrorDescription);

public sealed record OAuthCallbackData(
    string? Code,
    string? Token,
    string? State,
    string? Error,
    string? ErrorDescription)
{
    public string? Value => Token ?? Code;

    public bool MatchesState(string expectedState) =>
        !string.IsNullOrWhiteSpace(expectedState)
        && !string.IsNullOrWhiteSpace(State)
        && string.Equals(State, expectedState, StringComparison.Ordinal);
}
