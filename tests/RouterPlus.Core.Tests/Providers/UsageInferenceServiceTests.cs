using RouterPlus.Core.Providers;
using Xunit;

namespace RouterPlus.Core.Tests.Providers;

/// <summary>
/// TDD tests for UsageInferenceService
/// Infers quota usage from provider error messages
/// </summary>
public sealed class UsageInferenceServiceTests
{
    [Fact]
    public void InferUsageFromError_WithNullError_ReturnsNull()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            null,
            DateTimeOffset.UtcNow);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_WithEmptyError_ReturnsNull()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            "   ",
            DateTimeOffset.UtcNow);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_WithNonLimitError_ReturnsNull()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "500",
            "Internal server error",
            DateTimeOffset.UtcNow);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("429", "Some error")]
    [InlineData("402", "Some error")]
    [InlineData("200", "usage limit exceeded")]
    [InlineData("200", "You have reached the limit")]
    [InlineData("200", "You have reached your quota")]
    [InlineData("200", "Credits exhausted")]
    [InlineData("200", "Quota exceeded")]
    [InlineData("200", "Rate limit reached")]
    public void InferUsageFromError_DetectsLimitErrors(string errorCode, string errorMessage)
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            errorCode,
            errorMessage,
            DateTimeOffset.UtcNow);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsEstimate);
    }

    [Fact]
    public void InferUsageFromError_Codex_ReturnsMonthlyLimit()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            "[429]: The usage limit has been reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.True(result.IsEstimate);
        Assert.Equal("Inferred from error: usage limit reached", result.Source);

        // Reset should be Oct 1, 2026 00:00:00
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(2026, result.UsageResetAt.Value.Year);
        Assert.Equal(10, result.UsageResetAt.Value.Month);
        Assert.Equal(1, result.UsageResetAt.Value.Day);
    }

    [Fact]
    public void InferUsageFromError_Kiro_WithMonthlyRequestCount_ReturnsMonthlyLimit()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Kiro,
            "402",
            "[402]: {\"message\":\"You have reached the limit.\",\"reason\":\"MONTHLY_REQUEST_COUNT\"}",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.True(result.IsEstimate);
        Assert.Equal("Inferred from error: monthly request limit", result.Source);
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(10, result.UsageResetAt.Value.Month);
    }

    [Fact]
    public void InferUsageFromError_Kiro_WithoutMonthlyRequestCount_ReturnsNoResetTime()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Kiro,
            "402",
            "[402]: Generic limit error",
            DateTimeOffset.UtcNow);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Inferred from error: limit reached", result.Source);
        Assert.Null(result.UsageResetAt);
    }

    [Fact]
    public void InferUsageFromError_OpenRouter_ParsesCreditsFromMessage()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            "402",
            "You requested 2.50 credits but only have 1.25",
            errorTime);

        // Assert - verify it parsed successfully (not fallback)
        Assert.NotNull(result);
        Assert.True(result.IsEstimate);
        Assert.Equal("Parsed from error message", result.Source);

        // Note: OpenRouter API messages always use invariant decimal format.
        // Parsing must use InvariantCulture: under vi-VN, "2.50" would parse
        // as 250 (dot = thousands separator) and corrupt quota auto-disable.
        // requested=2.50, remaining=1.25 (as decimals, then * 100)
        // remainingCents = 125, requestedCents = 250
        // estimatedLimit = 125 + 250 + 1000 = 1375
        // usage = 1375 - 125 = 1250
        Assert.Equal(1250, result.UsageCount);
        Assert.Equal(1375, result.LimitCount);
    }

    [Fact]
    public void InferUsageFromError_OpenRouter_WithUnparsableMessage_ReturnsFallback()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            "402",
            "[402]: Credits exhausted",
            DateTimeOffset.UtcNow);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10000, result.UsageCount);
        Assert.Equal(10000, result.LimitCount);
        Assert.Equal("Inferred from error: credits exhausted", result.Source);
    }

    [Fact]
    public void InferUsageFromError_Ollama_WithWeeklyLimit_ReturnsWeeklyReset()
    {
        // Arrange - Sunday Sep 6, 2026
        var errorTime = new DateTimeOffset(2026, 9, 6, 10, 30, 0, TimeSpan.Zero);
        Assert.Equal(DayOfWeek.Sunday, errorTime.DayOfWeek);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "[429]: {\"error\":\"you have reached your weekly usage limit\"}",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Inferred from error: weekly limit", result.Source);
        Assert.NotNull(result.UsageResetAt);

        // Should reset next Monday (Sep 7)
        Assert.Equal(DayOfWeek.Monday, result.UsageResetAt.Value.DayOfWeek);
        Assert.Equal(7, result.UsageResetAt.Value.Day);
    }

    [Fact]
    public void InferUsageFromError_Ollama_WithSessionLimit_ReturnsDailyReset()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "[429]: Session limit reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Inferred from error: session limit", result.Source);
        Assert.NotNull(result.UsageResetAt);

        // Should reset next day (Sep 16) at midnight
        Assert.Equal(16, result.UsageResetAt.Value.Day);
        Assert.Equal(0, result.UsageResetAt.Value.Hour);
    }

    [Fact]
    public void InferUsageFromError_Ollama_GenericLimit_ReturnsDailyReset()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "Generic limit error",
            DateTimeOffset.UtcNow);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Inferred from error: limit reached", result.Source);
        Assert.NotNull(result.UsageResetAt);
    }

    [Fact]
    public void InferUsageFromError_Ollama_SessionLimitWithNullTimestamp_UsesCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;

        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "Session limit reached",
            null);

        var after = DateTimeOffset.UtcNow;
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        Assert.InRange(result.UsageResetAt.Value, before.Date.AddDays(1), after.Date.AddDays(2));
        Assert.Equal(0, result.UsageResetAt.Value.Hour);
        Assert.Equal(0, result.UsageResetAt.Value.Minute);
    }

    [Fact]
    public void InferUsageFromError_Kimchi_ReturnsMonthlyLimit()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Kimchi,
            "402",
            "[402]: {\"error\": \"the provider has exhausted its credits\"}",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.Equal("Inferred from error: credits exhausted", result.Source);
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(10, result.UsageResetAt.Value.Month);
    }

    [Fact]
    public void InferUsageFromError_OpenRouter_CreditDetailsMessage_KeepsUsageBelowLimit()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            "402",
            "[402]: You requested 5.50 credits but only have 2.30 remaining",
            errorTime);

        // Assert - the requested amount is parsed as the limit, so usage stays below it
        Assert.NotNull(result);
        Assert.True(result.UsageCount > 0);
        Assert.True(result.LimitCount > result.UsageCount);
    }

    [Fact]
    public void InferUsageFromError_Kimchi_JsonCreditsExhaustedMessage_ReturnsMonthlyLimit()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Kimchi,
            "402",
            "[402]: {\"error\": \"the provider for model kimi-k2.7 has exhausted its credits and cannot process requests\"}",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.UsageCount);
        Assert.Equal(100, result.LimitCount);
        Assert.True(result.IsEstimate);
    }

    [Fact]
    public void InferUsageFromError_UnsupportedProvider_ReturnsNullForLimitError()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.GitHub,
            "429",
            "Provider limit reached",
            errorTime);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_Ollama_WeeklyLimitOnMonday_ResetsFollowingMonday()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);
        Assert.Equal(DayOfWeek.Monday, errorTime.DayOfWeek);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "Weekly limit reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 0, 0, 0, errorTime.Offset), result.UsageResetAt.Value);
    }

    [Fact]
    public void InferUsageFromError_Ollama_SessionLimitAtYearBoundary_ResetsNextDay()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 12, 31, 23, 45, 0, TimeSpan.FromHours(5.5));

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "Session limit reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, errorTime.Offset), result.UsageResetAt.Value);
    }

    [Fact]
    public void InferUsageFromError_Codex_MonthlyResetPreservesOffset()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.FromHours(-7));

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            "Usage limit reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, errorTime.Offset), result.UsageResetAt.Value);
    }

    [Fact]
    public void InferUsageFromError_WithNullErrorTime_UsesCurrentTime()
    {
        // Arrange & Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            "Limit reached",
            null);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        // Should be sometime in the future
        Assert.True(result.UsageResetAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void InferUsageFromError_MonthlyReset_HandlesYearBoundary()
    {
        // Arrange - December 2026
        var errorTime = new DateTimeOffset(2026, 12, 25, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            "Limit reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);

        // Should reset to Jan 1, 2027
        Assert.Equal(2027, result.UsageResetAt.Value.Year);
        Assert.Equal(1, result.UsageResetAt.Value.Month);
        Assert.Equal(1, result.UsageResetAt.Value.Day);
    }

    [Fact]
    public void InferUsageFromError_WeeklyReset_OnMonday_ResetsNextMonday()
    {
        // Arrange - Wednesday Sep 2, 2026
        var errorTime = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        Assert.Equal(DayOfWeek.Wednesday, errorTime.DayOfWeek);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "weekly limit",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);

        // Should be next Monday (Sep 7)
        Assert.Equal(7, result.UsageResetAt.Value.Day);
        Assert.Equal(DayOfWeek.Monday, result.UsageResetAt.Value.DayOfWeek);
    }

    [Fact]
    public void InferUsageFromError_WeeklyReset_OnMonday_ResetsFollowingMonday()
    {
        // Arrange - Monday Sep 7, 2026
        var errorTime = new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);
        Assert.Equal(DayOfWeek.Monday, errorTime.DayOfWeek);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Ollama,
            "429",
            "weekly limit",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UsageResetAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), result.UsageResetAt);
    }

    [Fact]
    public void InferUsageFromError_WithUnsupportedProvider_ReturnsNull()
    {
        // Arrange
        var unsupportedProvider = (ProviderKind)999;

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            unsupportedProvider,
            "429",
            "usage limit exceeded",
            new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_CaseInsensitive_Keywords()
    {
        // Arrange & Act
        var result1 = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex, "200", "USAGE LIMIT EXCEEDED", DateTimeOffset.UtcNow);

        var result2 = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex, "200", "Reached Your Quota", DateTimeOffset.UtcNow);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Fact]
    public void InferUsageFromError_OpenRouter_ParsesWholeNumberCreditsDeterministically()
    {
        // Arrange
        var errorTime = new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.OpenRouter,
            "402",
            "You requested 5 credits but only have 2 remaining",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1_500, result.UsageCount);
        Assert.Equal(1_700, result.LimitCount);
        Assert.Equal("Parsed from error message", result.Source);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), result.UsageResetAt);
    }

    [Fact]
    public void InferredUsage_RecordProperties_AreAccessible()
    {
        // Arrange
        var resetTime = DateTimeOffset.UtcNow.AddDays(30);

        // Act
        var usage = new InferredUsage(
            UsageCount: 75,
            LimitCount: 100,
            UsageResetAt: resetTime,
            IsEstimate: true,
            Source: "Test source");

        // Assert
        Assert.Equal(75, usage.UsageCount);
        Assert.Equal(100, usage.LimitCount);
        Assert.Equal(resetTime, usage.UsageResetAt);
        Assert.True(usage.IsEstimate);
        Assert.Equal("Test source", usage.Source);
    }
}
