using System.Text.RegularExpressions;

namespace RouterPlus.Core.Providers;

/// <summary>
/// Infers usage quota information from provider error messages when backend doesn't provide explicit usage data.
/// This is a workaround until 9Router backend implements proper usage tracking API.
/// </summary>
public static class UsageInferenceService
{
    /// <summary>
    /// Attempts to infer usage data from error code and error message.
    /// Returns estimated usage when limits are reached, or null if no inference possible.
    /// </summary>
    public static InferredUsage? InferUsageFromError(
        ProviderKind provider,
        string? errorCode,
        string? lastError,
        DateTimeOffset? lastErrorAt)
    {
        if (string.IsNullOrWhiteSpace(lastError))
        {
            return null;
        }

        // Check if error indicates limit reached
        if (!IsLimitError(errorCode, lastError))
        {
            return null;
        }

        return provider switch
        {
            ProviderKind.Codex => InferCodexUsage(lastError, lastErrorAt),
            ProviderKind.Kiro => InferKiroUsage(lastError, lastErrorAt),
            ProviderKind.OpenRouter => InferOpenRouterUsage(lastError, lastErrorAt),
            ProviderKind.Ollama => InferOllamaUsage(lastError, lastErrorAt),
            ProviderKind.Kimchi => InferKimchiUsage(lastError, lastErrorAt),
            _ => null
        };
    }

    private static bool IsLimitError(string? errorCode, string lastError)
    {
        // 429 = Too Many Requests, 402 = Payment Required (over quota)
        if (errorCode is "429" or "402")
        {
            return true;
        }

        var lowerError = lastError.ToLowerInvariant();
        return lowerError.Contains("usage limit") ||
               lowerError.Contains("reached the limit") ||
               lowerError.Contains("reached your") ||
               lowerError.Contains("exhausted") ||
               lowerError.Contains("quota") ||
               lowerError.Contains("rate limit");
    }

    private static InferredUsage InferCodexUsage(string lastError, DateTimeOffset? lastErrorAt)
    {
        // Codex: "[429]: The usage limit has been reached"
        // Codex typically has monthly limits
        return new InferredUsage(
            UsageCount: 100,
            LimitCount: 100,
            UsageResetAt: CalculateNextMonthReset(lastErrorAt),
            IsEstimate: true,
            Source: "Inferred from error: usage limit reached"
        );
    }

    private static InferredUsage InferKiroUsage(string lastError, DateTimeOffset? lastErrorAt)
    {
        // Kiro: "[402]: {\"message\":\"You have reached the limit.\",\"reason\":\"MONTHLY_REQUEST_COUNT\"}"
        if (lastError.Contains("MONTHLY_REQUEST_COUNT", StringComparison.OrdinalIgnoreCase))
        {
            return new InferredUsage(
                UsageCount: 100,
                LimitCount: 100,
                UsageResetAt: CalculateNextMonthReset(lastErrorAt),
                IsEstimate: true,
                Source: "Inferred from error: monthly request limit"
            );
        }

        return new InferredUsage(
            UsageCount: 100,
            LimitCount: 100,
            UsageResetAt: null,
            IsEstimate: true,
            Source: "Inferred from error: limit reached"
        );
    }

    private static InferredUsage InferOpenRouterUsage(string lastError, DateTimeOffset? lastErrorAt)
    {
        // OpenRouter: "[402]: {\"error\":{\"message\":\"This request requires more credits..."
        // Try to extract credit info from error message
        var match = Regex.Match(lastError, @"You requested\s+([\d.]+)\s*credits?\s+but\s+.*have\s+([\d.]+)", 
            RegexOptions.IgnoreCase);

        if (match.Success && 
            decimal.TryParse(match.Groups[1].Value, out var requested) &&
            decimal.TryParse(match.Groups[2].Value, out var remaining))
        {
            // Convert dollars to cents for integer handling
            var remainingCents = (long)(remaining * 100);
            var requestedCents = (long)(requested * 100);
            var estimatedLimit = remainingCents + requestedCents + 1000; // Add buffer

            return new InferredUsage(
                UsageCount: estimatedLimit - remainingCents,
                LimitCount: estimatedLimit,
                UsageResetAt: CalculateNextMonthReset(lastErrorAt),
                IsEstimate: true,
                Source: "Parsed from error message"
            );
        }

        // Fallback: assume at limit
        return new InferredUsage(
            UsageCount: 10000,
            LimitCount: 10000,
            UsageResetAt: CalculateNextMonthReset(lastErrorAt),
            IsEstimate: true,
            Source: "Inferred from error: credits exhausted"
        );
    }

    private static InferredUsage InferOllamaUsage(string lastError, DateTimeOffset? lastErrorAt)
    {
        // Ollama: "[429]: {\"error\":\"you (username) have reached your weekly usage limit..."
        if (lastError.Contains("weekly", StringComparison.OrdinalIgnoreCase))
        {
            return new InferredUsage(
                UsageCount: 100,
                LimitCount: 100,
                UsageResetAt: CalculateNextWeekReset(lastErrorAt),
                IsEstimate: true,
                Source: "Inferred from error: weekly limit"
            );
        }

        if (lastError.Contains("session", StringComparison.OrdinalIgnoreCase))
        {
            return new InferredUsage(
                UsageCount: 100,
                LimitCount: 100,
                UsageResetAt: CalculateDailyReset(lastErrorAt),
                IsEstimate: true,
                Source: "Inferred from error: session limit"
            );
        }

        return new InferredUsage(
            UsageCount: 100,
            LimitCount: 100,
            UsageResetAt: CalculateDailyReset(lastErrorAt),
            IsEstimate: true,
            Source: "Inferred from error: limit reached"
        );
    }

    private static InferredUsage InferKimchiUsage(string lastError, DateTimeOffset? lastErrorAt)
    {
        // Kimchi: "[402]: {\"error\": \"the provider for model X has exhausted its credits..."
        return new InferredUsage(
            UsageCount: 100,
            LimitCount: 100,
            UsageResetAt: CalculateNextMonthReset(lastErrorAt),
            IsEstimate: true,
            Source: "Inferred from error: credits exhausted"
        );
    }

    private static DateTimeOffset CalculateNextMonthReset(DateTimeOffset? reference)
    {
        var now = reference ?? DateTimeOffset.Now;
        var nextMonth = new DateTimeOffset(
            now.Year,
            now.Month,
            1,
            0, 0, 0,
            now.Offset
        ).AddMonths(1);
        return nextMonth;
    }

    private static DateTimeOffset CalculateNextWeekReset(DateTimeOffset? reference)
    {
        var now = reference ?? DateTimeOffset.Now;
        var daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilNextMonday == 0) daysUntilNextMonday = 7;
        
        return new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            0, 0, 0,
            now.Offset
        ).AddDays(daysUntilNextMonday);
    }

    private static DateTimeOffset CalculateDailyReset(DateTimeOffset? reference)
    {
        var now = reference ?? DateTimeOffset.Now;
        return new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            0, 0, 0,
            now.Offset
        ).AddDays(1);
    }
}

/// <summary>
/// Represents usage data inferred from error messages.
/// </summary>
public sealed record InferredUsage(
    long UsageCount,
    long LimitCount,
    DateTimeOffset? UsageResetAt,
    bool IsEstimate,
    string Source);
