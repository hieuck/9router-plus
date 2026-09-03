namespace RouterPlus.Core.Security;

/// <summary>
/// Result of a credential health check operation.
/// Verifies if stored credentials are still valid by attempting a test login.
/// </summary>
public sealed record CredentialHealthCheckResult
{
    private CredentialHealthCheckResult(
        CredentialHealthStatus status,
        string message,
        DateTime? lastChecked = null,
        Exception? error = null)
    {
        Status = status;
        Message = message;
        LastChecked = lastChecked ?? DateTime.UtcNow;
        Exception = error;
    }

    public CredentialHealthStatus Status { get; }
    public string Message { get; }
    public DateTime LastChecked { get; }
    public Exception? Exception { get; }

    public static CredentialHealthCheckResult Healthy(string message = "Credentials are valid")
        => new(CredentialHealthStatus.Healthy, message);

    public static CredentialHealthCheckResult Invalid(string message = "Invalid credentials")
        => new(CredentialHealthStatus.Invalid, message);

    public static CredentialHealthCheckResult Expired(string message = "Credentials expired")
        => new(CredentialHealthStatus.Expired, message);

    public static CredentialHealthCheckResult RequiresAction(string message)
        => new(CredentialHealthStatus.RequiresAction, message);

    public static CredentialHealthCheckResult Unknown(string message = "Health status unknown")
        => new(CredentialHealthStatus.Unknown, message);

    public static CredentialHealthCheckResult Checking(string message = "Checking credentials...")
        => new(CredentialHealthStatus.Checking, message);

    public static CredentialHealthCheckResult Error(string message, Exception? error = null)
        => new(CredentialHealthStatus.Error, message, error: error);

    public static CredentialHealthCheckResult NotConfigured(string message = "No credentials configured")
        => new(CredentialHealthStatus.NotConfigured, message);
}

/// <summary>
/// Health status of stored credentials.
/// </summary>
public enum CredentialHealthStatus
{
    /// <summary>
    /// Health status has not been checked yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// Health check is currently in progress.
    /// </summary>
    Checking,

    /// <summary>
    /// Credentials are valid and working.
    /// </summary>
    Healthy,

    /// <summary>
    /// Credentials are invalid (wrong password, etc.).
    /// </summary>
    Invalid,

    /// <summary>
    /// Credentials have expired and need renewal.
    /// </summary>
    Expired,

    /// <summary>
    /// Credentials require manual action (CAPTCHA, 2FA, etc.).
    /// </summary>
    RequiresAction,

    /// <summary>
    /// No credentials configured for this profile.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// Health check failed due to system error.
    /// </summary>
    Error
}

/// <summary>
/// Extensions for CredentialHealthStatus.
/// </summary>
public static class CredentialHealthStatusExtensions
{
    /// <summary>
    /// Gets a user-friendly display text for the health status.
    /// </summary>
    public static string ToDisplayText(this CredentialHealthStatus status) => status switch
    {
        CredentialHealthStatus.Unknown => "Unknown",
        CredentialHealthStatus.Checking => "Checking...",
        CredentialHealthStatus.Healthy => "✓ Healthy",
        CredentialHealthStatus.Invalid => "✗ Invalid",
        CredentialHealthStatus.Expired => "⚠ Expired",
        CredentialHealthStatus.RequiresAction => "⚠ Action Required",
        CredentialHealthStatus.NotConfigured => "Not Configured",
        CredentialHealthStatus.Error => "✗ Error",
        _ => status.ToString()
    };

    /// <summary>
    /// Gets an emoji indicator for the health status.
    /// </summary>
    public static string ToEmoji(this CredentialHealthStatus status) => status switch
    {
        CredentialHealthStatus.Healthy => "✓",
        CredentialHealthStatus.Invalid => "✗",
        CredentialHealthStatus.Expired => "⚠",
        CredentialHealthStatus.RequiresAction => "⚠",
        CredentialHealthStatus.Checking => "⟳",
        CredentialHealthStatus.NotConfigured => "○",
        CredentialHealthStatus.Error => "✗",
        CredentialHealthStatus.Unknown => "?",
        _ => "?"
    };

    /// <summary>
    /// Determines if the status indicates a problem that needs attention.
    /// </summary>
    public static bool IsHealthy(this CredentialHealthStatus status) => status switch
    {
        CredentialHealthStatus.Healthy => true,
        _ => false
    };

    /// <summary>
    /// Determines if the status indicates credentials need to be fixed.
    /// </summary>
    public static bool NeedsAttention(this CredentialHealthStatus status) => status switch
    {
        CredentialHealthStatus.Invalid => true,
        CredentialHealthStatus.Expired => true,
        CredentialHealthStatus.RequiresAction => true,
        CredentialHealthStatus.Error => true,
        _ => false
    };
}
