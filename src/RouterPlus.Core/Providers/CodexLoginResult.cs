namespace RouterPlus.Core.Providers;

/// <summary>
/// Result of Codex login automation from Credentials Manager.
/// Similar to GoogleLoginResult but for provider OAuth/direct login.
/// </summary>
public sealed class CodexLoginResult
{
    public CodexLoginResultCategory Category { get; }
    public string Message { get; }

    private CodexLoginResult(CodexLoginResultCategory category, string message)
    {
        Category = category;
        Message = message;
    }

    public static CodexLoginResult Success() =>
        new(CodexLoginResultCategory.Success, "Codex login successful");

    public static CodexLoginResult ManualInterventionRequired(string reason) =>
        new(CodexLoginResultCategory.ManualInterventionRequired, reason);

    public static CodexLoginResult Timeout() =>
        new(CodexLoginResultCategory.Timeout, "Codex login timed out");

    public static CodexLoginResult Cancelled() =>
        new(CodexLoginResultCategory.Cancelled, "Codex login cancelled");

    public static CodexLoginResult Failed(string reason) =>
        new(CodexLoginResultCategory.Failed, reason);
}

public enum CodexLoginResultCategory
{
    Success,
    ManualInterventionRequired,
    Timeout,
    Cancelled,
    Failed
}
