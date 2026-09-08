using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class UsageInferenceServiceAdditionalTests
{
    [Fact]
    public void InferUsageFromError_ReturnsNullForUnsupportedProviderWithLimitError()
    {
        // Arrange
        const ProviderKind unsupportedProvider = ProviderKind.GitHub;
        const string errorCode = "429";
        const string errorMessage = "synthetic usage limit reached";

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            unsupportedProvider,
            errorCode,
            errorMessage,
            DateTimeOffset.Parse("2026-09-15T10:30:00-05:00"));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InferUsageFromError_PreservesReferenceOffsetWhenCalculatingMonthlyReset()
    {
        // Arrange
        var errorTime = DateTimeOffset.Parse("2026-09-15T10:30:00-05:00");

        // Act
        var result = UsageInferenceService.InferUsageFromError(
            ProviderKind.Codex,
            "429",
            "synthetic limit reached",
            errorTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00-05:00"), result.UsageResetAt);
    }
}
