namespace RouterPlus.Core.Chrome;

/// <summary>
/// Represents a specific health issue found during profile health check.
/// </summary>
public sealed record HealthIssue
{
    /// <summary>
    /// Category of health check that found this issue.
    /// </summary>
    public HealthCategory Category { get; init; }

    /// <summary>
    /// Severity level of this issue.
    /// </summary>
    public IssueSeverity Severity { get; init; }

    /// <summary>
    /// Human-readable description of the issue.
    /// Example: "Profile directory not found"
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Optional recommendation for resolving the issue.
    /// Example: "Profile may have been deleted externally. Consider removing from catalog."
    /// </summary>
    public string? Recommendation { get; init; }

    /// <summary>
    /// Create an informational issue (severity: Info).
    /// </summary>
    public static HealthIssue Info(HealthCategory category, string description)
        => new() { Category = category, Severity = IssueSeverity.Info, Description = description };

    /// <summary>
    /// Create a warning issue (severity: Warning).
    /// </summary>
    public static HealthIssue Warning(HealthCategory category, string description, string? recommendation = null)
        => new() { Category = category, Severity = IssueSeverity.Warning, Description = description, Recommendation = recommendation };

    /// <summary>
    /// Create an error issue (severity: Error).
    /// </summary>
    public static HealthIssue Error(HealthCategory category, string description, string? recommendation = null)
        => new() { Category = category, Severity = IssueSeverity.Error, Description = description, Recommendation = recommendation };
}

/// <summary>
/// Category of health check.
/// </summary>
public enum HealthCategory
{
    /// <summary>
    /// Filesystem accessibility checks (directory exists, files readable, etc.).
    /// </summary>
    Filesystem,

    /// <summary>
    /// Vault integrity checks (vault files exist, decryptable, etc.).
    /// </summary>
    Vault,

    /// <summary>
    /// Credentials configuration checks (credentials present, valid, etc.).
    /// </summary>
    Credentials,

    /// <summary>
    /// Provider health checks (connections active, test status, etc.).
    /// </summary>
    Provider
}

/// <summary>
/// Severity level of a health issue.
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// Informational only, no action required.
    /// </summary>
    Info,

    /// <summary>
    /// Minor issue, profile may still be usable.
    /// </summary>
    Warning,

    /// <summary>
    /// Critical issue, profile likely unusable.
    /// </summary>
    Error
}
