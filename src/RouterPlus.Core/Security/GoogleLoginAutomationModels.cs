namespace RouterPlus.Core.Security;

/// <summary>
/// Represents the current state of the Google login page.
/// </summary>
public sealed record GoogleLoginPageState(
    Uri PageUri,
    bool HasEmailField,
    bool HasPasswordField,
    bool HasTotpField,
    bool HasTotpError,
    bool Has2FAMethodPicker,
    bool HasCompletionSignal,
    bool HasManualChallenge);

/// <summary>
/// Semantic field identifiers for Google login form.
/// </summary>
public enum GoogleLoginField
{
    Email,
    Password,
    Totp
}

/// <summary>
/// Result of a Google login automation attempt.
/// </summary>
public sealed record GoogleLoginResult
{
    private GoogleLoginResult(GoogleLoginResultCategory category, string message)
    {
        Category = category;
        Message = message;
    }

    public GoogleLoginResultCategory Category { get; }
    public string Message { get; }

    public static GoogleLoginResult Success()
        => new(GoogleLoginResultCategory.Success, "Login completed successfully.");

    public static GoogleLoginResult ManualInterventionRequired(string reason)
        => new(GoogleLoginResultCategory.ManualInterventionRequired, reason);

    public static GoogleLoginResult InvalidCredentials()
        => new(GoogleLoginResultCategory.InvalidCredentials, "Invalid email, password, or TOTP code.");

    public static GoogleLoginResult Timeout()
        => new(GoogleLoginResultCategory.Timeout, "Login automation timed out.");

    public static GoogleLoginResult Cancelled()
        => new(GoogleLoginResultCategory.Cancelled, "Login automation was cancelled.");

    public static GoogleLoginResult BrowserDisconnected()
        => BrowserDisconnected("Browser connection was lost.");

    public static GoogleLoginResult BrowserDisconnected(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(GoogleLoginResultCategory.BrowserDisconnected, message);
    }

    public static GoogleLoginResult UnsupportedPage(string reason)
        => new(GoogleLoginResultCategory.UnsupportedPage, reason);
}

/// <summary>
/// Safe categorization of login automation outcomes.
/// </summary>
public enum GoogleLoginResultCategory
{
    Success,
    ManualInterventionRequired,
    InvalidCredentials,
    Timeout,
    Cancelled,
    BrowserDisconnected,
    UnsupportedPage
}
