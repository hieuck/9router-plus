using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class UsageInferenceServiceTests
{
    [Fact]
    public void InferUsageFromError_returns_null_when_no_error()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            errorCode: null,
            lastError: null,
            lastErrorAt: null);

        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_returns_null_for_non_limit_errors()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            errorCode: "400",
            lastError: "[400]: Bad request - invalid parameter",
            lastErrorAt: DateTimeOffset.Now);

        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_detects_codex_usage_limit()
    {
        var lastErrorAt = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            errorCode: "429",
            lastError: "[429]: The usage limit has been reached",
            lastErrorAt: lastErrorAt);

        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.True(result.IsEstimate);
        Assert.NotNull(result.UsageResetAt);
        // Should reset on first of next month
        Assert.Equal(9, result.UsageResetAt.Value.Month);
    }

    [Fact]
    public void InferUsageFromError_detects_kiro_monthly_limit()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Kiro,
            errorCode: "402",
            lastError: "[402]: {\"message\":\"You have reached the limit.\",\"reason\":\"MONTHLY_REQUEST_COUNT\"}",
            lastErrorAt: DateTimeOffset.Now);

        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.True(result.IsEstimate);
        Assert.Contains("monthly", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferUsageFromError_detects_openrouter_credit_exhausted()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            errorCode: "402",
            lastError: "[402]: {\"error\":{\"message\":\"This request requires more credits, or fewer max_tokens.\"}}",
            lastErrorAt: DateTimeOffset.Now);

        Assert.NotNull(result);
        Assert.True(result.UsageCount > 0);
        Assert.True(result.LimitCount > 0);
        Assert.True(result.IsEstimate);
    }

    [Fact]
    public void InferUsageFromError_parses_openrouter_credit_details()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            errorCode: "402",
            lastError: "[402]: You requested 5.50 credits but only have 2.30 remaining",
            lastErrorAt: DateTimeOffset.Now);

        Assert.NotNull(result);
        // Should parse: 5.50 requested, 2.30 remaining
        // usageCount should be (limit - remaining)
        Assert.True(result.UsageCount > 0);
        Assert.True(result.LimitCount > result.UsageCount);
    }

    [Fact]
    public void InferUsageFromError_detects_ollama_weekly_limit()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            errorCode: "429",
            lastError: "[429]: {\"error\":\"you (username) have reached your weekly usage limit, upgrade for higher limits\"}",
            lastErrorAt: DateTimeOffset.Now);

        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.NotNull(result.UsageResetAt);
        Assert.Contains("weekly", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferUsageFromError_detects_ollama_session_limit()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            errorCode: "429",
            lastError: "[429]: {\"error\":\"you have reached your session usage limit\"}",
            lastErrorAt: DateTimeOffset.Now);

        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.Contains("session", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferUsageFromError_detects_kimchi_credits_exhausted()
    {
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Kimchi,
            errorCode: "402",
            lastError: "[402]: {\"error\": \"the provider for model kimi-k2.7 has exhausted its credits and cannot process requests\"}",
            lastErrorAt: DateTimeOffset.Now);

        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.True(result.IsEstimate);
    }

    [Fact]
    public void InferUsageFromError_calculates_monthly_reset_correctly()
    {
        var lastErrorAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            errorCode: "429",
            lastError: "[429]: The usage limit has been reached",
            lastErrorAt: lastErrorAt);

        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        
        // Should reset on September 1, 2026 00:00:00
        Assert.Equal(2026, result.UsageResetAt.Value.Year);
        Assert.Equal(9, result.UsageResetAt.Value.Month);
        Assert.Equal(1, result.UsageResetAt.Value.Day);
        Assert.Equal(0, result.UsageResetAt.Value.Hour);
    }
}
