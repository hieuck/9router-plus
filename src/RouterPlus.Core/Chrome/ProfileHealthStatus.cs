namespace RouterPlus.Core.Chrome;

/// <summary>
/// Health status result for a Chrome profile.
/// Aggregates checks across filesystem, vault, credentials, and providers.
/// </summary>
public sealed record ProfileHealthStatus
{
    /// <summary>
    /// Overall health level (Healthy/Warning/Error/Unknown).
    /// Computed from highest severity issue present.
    /// </summary>
    public HealthLevel Level { get; init; }

    /// <summary>
    /// Human-readable summary message.
    /// Example: "Profile accessible, 2 credentials configured"
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// When this health check was performed (UTC).
    /// </summary>
    public DateTime LastChecked { get; init; }

    /// <summary>
    /// Detailed issues found during health check.
    /// Empty if Level = Healthy.
    /// </summary>
    public IReadOnlyList<HealthIssue> Issues { get; init; } = Array.Empty<HealthIssue>();

    /// <summary>
    /// Create healthy status with no issues.
    /// </summary>
    public static ProfileHealthStatus Healthy(string message)
        => new()
        {
            Level = HealthLevel.Healthy,
            Message = message,
            LastChecked = DateTime.UtcNow,
            Issues = Array.Empty<HealthIssue>()
        };

    /// <summary>
    /// Create status from list of issues.
    /// Level computed from highest severity issue.
    /// </summary>
    public static ProfileHealthStatus FromIssues(IEnumerable<HealthIssue> issues)
    {
        var issueList = issues.ToArray();
        var level = ComputeHealthLevel(issueList);
        var message = FormatSummaryMessage(level, issueList);

        return new ProfileHealthStatus
        {
            Level = level,
            Message = message,
            LastChecked = DateTime.UtcNow,
            Issues = issueList
        };
    }

    private static HealthLevel ComputeHealthLevel(IReadOnlyList<HealthIssue> issues)
    {
        if (issues.Count == 0) return HealthLevel.Healthy;
        if (issues.Any(i => i.Severity == IssueSeverity.Error)) return HealthLevel.Error;
        if (issues.Any(i => i.Severity == IssueSeverity.Warning)) return HealthLevel.Warning;
        return HealthLevel.Healthy;
    }

    private static string FormatSummaryMessage(HealthLevel level, IReadOnlyList<HealthIssue> issues)
    {
        return level switch
        {
            HealthLevel.Healthy => "Profile healthy",
            HealthLevel.Warning => $"{issues.Count} warning(s) detected",
            HealthLevel.Error => $"{issues.Count(i => i.Severity == IssueSeverity.Error)} error(s) detected",
            HealthLevel.Unknown => "Health status unknown",
            _ => "Unknown status"
        };
    }
}

/// <summary>
/// Overall health level for a profile.
/// </summary>
public enum HealthLevel
{
    /// <summary>
    /// Health status has not been determined yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// All checks passed, no issues found.
    /// </summary>
    Healthy,

    /// <summary>
    /// Minor issues detected (non-critical).
    /// Profile may still be usable but needs attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Critical issues detected.
    /// Profile likely unusable until resolved.
    /// </summary>
    Error
}
