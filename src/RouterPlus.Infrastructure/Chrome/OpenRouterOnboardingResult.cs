namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Result of the OpenRouter key onboarding automation.
/// </summary>
public sealed record OpenRouterOnboardingResult(
    bool Success,
    string? ApiKey,
    string? ErrorMessage)
{
    public static OpenRouterOnboardingResult Succeeded(string apiKey) =>
        new(Success: true, ApiKey: apiKey, ErrorMessage: null);

    public static OpenRouterOnboardingResult Failed(string errorMessage) =>
        new(Success: false, ApiKey: null, ErrorMessage: errorMessage);
}