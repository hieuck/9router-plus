using RouterPlus.Core.Providers;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class ProviderConnectionStatusTests
{
    [Fact]
    public void IsDisabled_IsInverseOfIsActive()
    {
        // Arrange
        var activeConnection = CreateConnection(isActive: true);
        var inactiveConnection = CreateConnection(isActive: false);

        // Act
        var activeIsDisabled = activeConnection.IsDisabled;
        var inactiveIsDisabled = inactiveConnection.IsDisabled;

        // Assert
        Assert.False(activeIsDisabled);
        Assert.True(inactiveIsDisabled);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("OK")]
    [InlineData(" healthy ")]
    [InlineData("Available")]
    [InlineData("ready")]
    [InlineData("success")]
    [InlineData("connected")]
    public void HasSuccessfulTestStatus_ReturnsTrueForSuccessfulStatus(string status)
    {
        // Arrange
        var connection = CreateConnection(testStatus: status);

        // Act
        var result = connection.HasSuccessfulTestStatus;

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pending")]
    public void HasUnknownTestStatus_ReturnsTrueForMissingOrUnrecognizedStatus(string? status)
    {
        // Arrange
        var connection = CreateConnection(testStatus: status);

        // Act
        var result = connection.HasUnknownTestStatus;

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("expired")]
    [InlineData("unavailable")]
    [InlineData("invalid")]
    [InlineData("failed")]
    public void HasError_ReturnsTrueForKnownErrorStatus(string status)
    {
        // Arrange
        var connection = CreateConnection(testStatus: status);

        // Act
        var result = connection.HasError;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasError_ReturnsTrueWhenErrorCodeIsPresentForUnsuccessfulStatus()
    {
        // Arrange
        var connection = CreateConnection(testStatus: "pending", errorCode: "synthetic_error");

        // Act
        var result = connection.HasError;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasError_ReturnsFalseWhenSuccessfulStatusHasErrorDetails()
    {
        // Arrange
        var connection = CreateConnection(
            testStatus: "healthy",
            errorCode: "synthetic_error",
            lastError: "synthetic failure");

        // Act
        var result = connection.HasError;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UsagePercentage_UsesFirstQuotaWithPercentageAndSkipsQuotaWithoutTotal()
    {
        // Arrange
        var connection = CreateConnection(
            usageCount: 10,
            limitCount: 20,
            quotas:
            [
                new ProviderQuota("without-total", 10m, 0m, null, null),
                new ProviderQuota("synthetic-quota", 25m, 40m, 15m, null)
            ]);

        // Act
        var result = connection.UsagePercentage;

        // Assert
        Assert.Equal(62.5, result);
    }

    [Fact]
    public void IsNearLimit_ReturnsTrueWhenQuotaPercentageReachesThreshold()
    {
        // Arrange
        var connection = CreateConnection(
            quotas: [new ProviderQuota("synthetic-quota", 80m, 100m, 20m, null)]);

        // Act
        var result = connection.IsNearLimit;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsOverLimit_ReturnsTrueWhenQuotaRemainingIsNegative()
    {
        // Arrange
        var connection = CreateConnection(
            quotas: [new ProviderQuota("synthetic-quota", 50m, 100m, -1m, null)]);

        // Act
        var result = connection.IsOverLimit;

        // Assert
        Assert.True(result);
    }

    private static ProviderConnection CreateConnection(
        bool isActive = true,
        string? testStatus = null,
        string? errorCode = null,
        string? lastError = null,
        long? usageCount = null,
        long? limitCount = null,
        IReadOnlyList<ProviderQuota>? quotas = null) =>
        new(
            "synthetic-connection",
            ProviderKind.Codex,
            "Synthetic connection",
            1,
            isActive,
            TestStatus: testStatus,
            ErrorCode: errorCode,
            LastError: lastError,
            UsageCount: usageCount,
            LimitCount: limitCount,
            Quotas: quotas);
}
