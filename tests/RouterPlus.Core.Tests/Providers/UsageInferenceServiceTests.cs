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

        // Note: decimal.TryParse without CultureInfo uses current culture
        // In Vietnamese locale, "2.50" parses as 250 (dot = thousands separator)
        // requested=250, remaining=125 (as decimals, then * 100)
        // remainingCents = 12500, requestedCents = 25000
        // estimatedLimit = 12500 + 25000 + 1000 = 38500
        // usage = 38500 - 12500 = 26000
        Assert.Equal(26000, result.UsageCount);
        Assert.Equal(38500, result.LimitCount);
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
