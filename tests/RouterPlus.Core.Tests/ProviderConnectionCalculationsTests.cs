using System.Globalization;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

/// <summary>
/// Comprehensive tests for ProviderConnection and ProviderQuota data calculation properties.
/// Focuses on usage percentage, limit detection, and quota aggregation logic.
/// </summary>
public class ProviderConnectionCalculationsTests
{
    [Fact]
    public void HasUsageData_ReturnsTrue_WhenQuotaRowsExist()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            Quotas: [new ProviderQuota("requests", 50m, 100m, 50m, null)]);

        Assert.True(connection.HasUsageData);
    }

    [Fact]
    public void HasUsageData_ReturnsTrue_WhenUsageCountExists()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 50,
            LimitCount: 100);

        Assert.True(connection.HasUsageData);
    }

    [Fact]
    public void HasUsageData_ReturnsFalse_WhenNoDataExists()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true);

        Assert.False(connection.HasUsageData);
    }

    [Fact]
    public void UsagePercentage_CalculatesFromQuotaRows_WhenAvailable()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            Quotas: [new ProviderQuota("requests", 75m, 100m, 25m, null)]);

        Assert.Equal(75.0, connection.UsagePercentage);
    }

    [Fact]
    public void UsagePercentage_CalculatesFromUsageCount_WhenQuotaRowsUnavailable()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 80,
            LimitCount: 100);

        Assert.Equal(80.0, connection.UsagePercentage);
    }

    [Fact]
    public void UsagePercentage_ReturnsNull_WhenLimitCountIsZero()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 50,
            LimitCount: 0);

        Assert.Null(connection.UsagePercentage);
    }

    [Fact]
    public void UsagePercentage_ReturnsNull_WhenNoData()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true);

        Assert.Null(connection.UsagePercentage);
    }

    [Fact]
    public void IsNearLimit_ReturnsTrue_WhenUsageAbove80Percent()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 85,
            LimitCount: 100);

        Assert.True(connection.IsNearLimit);
    }

    [Fact]
    public void IsNearLimit_ReturnsTrue_WhenExactly80Percent()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 80,
            LimitCount: 100);

        Assert.True(connection.IsNearLimit);
    }

    [Fact]
    public void IsNearLimit_ReturnsFalse_WhenUsageBelow80Percent()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 70,
            LimitCount: 100);

        Assert.False(connection.IsNearLimit);
    }

    [Fact]
    public void IsNearLimit_ChecksAllQuotaRows()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Kiro,
            "Test",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("daily", 50m, 100m, 50m, null),
                new ProviderQuota("monthly", 900m, 1000m, 100m, null)
            ]);

        Assert.True(connection.IsNearLimit);
    }

    [Fact]
    public void IsOverLimit_ReturnsTrue_WhenUsageAt100Percent()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 100,
            LimitCount: 100);

        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void IsOverLimit_ReturnsTrue_WhenUsageAbove100Percent()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 110,
            LimitCount: 100);

        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void IsOverLimit_ReturnsFalse_WhenUsageBelow100Percent()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 95,
            LimitCount: 100);

        Assert.False(connection.IsOverLimit);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(4999, 10000)]
    [InlineData(7999, 10000)]
    public void IsNearLimit_ReturnsFalse_WhenJustBelowThreshold(long usageCount, long limitCount)
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: usageCount,
            LimitCount: limitCount);

        Assert.False(connection.IsNearLimit);
    }

    [Theory]
    [InlineData(80, 100)]
    [InlineData(855, 1000)]
    [InlineData(999, 1000)]
    public void IsNearLimit_ReturnsTrue_WhenAtOrAboveThreshold(long usageCount, long limitCount)
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: usageCount,
            LimitCount: limitCount);

        Assert.True(connection.IsNearLimit);
    }

    [Fact]
    public void ProviderQuota_FormattingUsesExplicitCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var quota = new ProviderQuota("credits", 49.54m, 50m, 0.46m, null);

            Assert.Equal("49,54 / 50", quota.UsageText);
            Assert.Equal("99,08%", quota.PercentageText);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ProviderConnection_PrioritizesQuotaDataOverLegacyUsageCount()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Kiro,
            "Test",
            1,
            true,
            UsageCount: 50,
            LimitCount: 100,
            Quotas:
            [
                new ProviderQuota("credit", 49.54m, 50m, 0.46m, null)
            ]);

        Assert.NotNull(connection.UsagePercentage);
        Assert.True(connection.UsagePercentage.Value > 99);
    }

    [Fact]
    public void ProviderQuota_CalculatesUsagePercentage_Correctly()
    {
        var quota = new ProviderQuota("requests", 75m, 100m, 25m, null);

        Assert.Equal(75m, quota.UsagePercentage);
    }

    [Fact]
    public void ProviderQuota_UsagePercentage_ReturnsNull_WhenTotalIsZero()
    {
        var quota = new ProviderQuota("requests", 50m, 0m, null, null);

        Assert.Null(quota.UsagePercentage);
    }

    [Fact]
    public void ProviderQuota_UsagePercentage_HandlesDecimalPrecision()
    {
        var quota = new ProviderQuota("credits", 49.54m, 50m, 0.46m, null);

        Assert.NotNull(quota.UsagePercentage);
        Assert.Equal(99.08m, quota.UsagePercentage.Value);
    }

    [Fact]
    public void ProviderQuota_UsageText_FormatsCorrectly()
    {
        var quota = new ProviderQuota("requests", 750m, 1000m, 250m, null);

        Assert.Equal("750 / 1000", quota.UsageText);
    }

    [Fact]
    public void ProviderQuota_UsageText_HandlesDecimals()
    {
        var quota = new ProviderQuota("credits", 49.54m, 50m, 0.46m, null);

        var expected = $"{49.54m.ToString("0.##", CultureInfo.CurrentCulture)} / {50m.ToString("0.##", CultureInfo.CurrentCulture)}";
        Assert.Equal(expected, quota.UsageText);
    }

    [Fact]
    public void ProviderQuota_UsageText_ReturnsPlaceholder_WhenNoData()
    {
        var quota = new ProviderQuota("requests", null, null, null, null);

        Assert.Equal("Chưa có dữ liệu", quota.UsageText);
    }

    [Fact]
    public void ProviderQuota_PercentageText_FormatsWithDecimals()
    {
        var quota = new ProviderQuota("requests", 75.567m, 100m, 24.433m, null);

        var expected = $"{quota.UsagePercentage!.Value:0.##}%";
        Assert.Equal(expected, quota.PercentageText);
    }

    [Fact]
    public void ProviderQuota_PercentageText_ReturnsPlaceholder_WhenNull()
    {
        var quota = new ProviderQuota("requests", null, null, null, null);

        Assert.Equal("—", quota.PercentageText);
    }

    [Fact]
    public void ProviderQuota_ResetText_ReturnsNA_WhenNull()
    {
        var quota = new ProviderQuota("requests", 750m, 1000m, 250m, null);

        Assert.Equal("N/A", quota.ResetText);
    }

    [Fact]
    public void ProviderQuota_IsNearLimit_WhenRemainingIsZero()
    {
        var quota = new ProviderQuota("requests", null, null, 0m, null);

        Assert.True(quota.IsNearLimit);
    }

    [Fact]
    public void ProviderQuota_IsNearLimit_WhenRemainingIsNegative()
    {
        var quota = new ProviderQuota("requests", 105m, 100m, -5m, null);

        Assert.True(quota.IsNearLimit);
    }

    [Fact]
    public void ProviderQuota_IsOverLimit_WhenRemainingIsZeroOrLess()
    {
        var quotaZero = new ProviderQuota("requests", null, null, 0m, null);
        var quotaNegative = new ProviderQuota("requests", null, null, -5m, null);

        Assert.True(quotaZero.IsOverLimit);
        Assert.True(quotaNegative.IsOverLimit);
    }

    [Fact]
    public void QuotaRows_ReturnsEmptyList_WhenQuotasIsNull()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            Quotas: null);

        Assert.NotNull(connection.QuotaRows);
        Assert.Empty(connection.QuotaRows);
    }

    [Fact]
    public void ProviderConnection_HandlesMultipleQuotaTypes()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Kiro,
            "Test",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("credit", 40m, 50m, 10m, null),
                new ProviderQuota("credit_freetrial", 450m, 500m, 50m, null),
                new ProviderQuota("requests", 50m, 100m, 50m, null)
            ]);

        Assert.Equal(3, connection.QuotaRows.Count);
        Assert.True(connection.HasUsageData);
        Assert.True(connection.IsNearLimit);
    }

    [Fact]
    public void UsagePercentage_HandlesNullUsageCount()
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: null,
            LimitCount: 100);

        Assert.Equal(0.0, connection.UsagePercentage);
    }

    [Fact]
    public void ProviderQuota_HandlesNullUsedValue()
    {
        var quota = new ProviderQuota("requests", null, 100m, 100m, null);

        Assert.Equal(0m, quota.UsagePercentage);
    }

    [Theory]
    [InlineData("active")]
    [InlineData(" OK ")]
    [InlineData("healthy")]
    [InlineData("available")]
    [InlineData("ready")]
    [InlineData("success")]
    [InlineData("connected")]
    public void HasSuccessfulTestStatus_ReturnsTrue_ForRecognizedStatusIgnoringCaseAndWhitespace(string status)
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
    [InlineData("unknown")]
    public void HasSuccessfulTestStatus_ReturnsFalse_ForMissingOrUnrecognizedStatus(string? status)
    {
        // Arrange
        var connection = CreateConnection(testStatus: status);

        // Act
        var result = connection.HasSuccessfulTestStatus;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasUnknownTestStatus_ReturnsTrue_WhenStatusIsMissingOrUnrecognizedWithoutError()
    {
        // Arrange
        var missingStatus = CreateConnection();
        var unrecognizedStatus = CreateConnection(testStatus: "pending");

        // Act
        var missingResult = missingStatus.HasUnknownTestStatus;
        var unrecognizedResult = unrecognizedStatus.HasUnknownTestStatus;

        // Assert
        Assert.True(missingResult);
        Assert.True(unrecognizedResult);
    }

    [Fact]
    public void HasUnknownTestStatus_ReturnsFalse_WhenStatusIsSuccessfulOrErrored()
    {
        // Arrange
        var successful = CreateConnection(testStatus: "ready");
        var errored = CreateConnection(testStatus: "failed");

        // Act
        var successfulResult = successful.HasUnknownTestStatus;
        var erroredResult = errored.HasUnknownTestStatus;

        // Assert
        Assert.False(successfulResult);
        Assert.False(erroredResult);
    }

    [Theory]
    [InlineData("error", null, null)]
    [InlineData("expired", null, null)]
    [InlineData("unavailable", null, null)]
    [InlineData("invalid", null, null)]
    [InlineData("failed", null, null)]
    [InlineData("pending", "AUTH_ERROR", null)]
    [InlineData("pending", null, "request failed")]
    public void HasError_ReturnsTrue_ForErrorStatusesOrErrorDetails(
        string? status,
        string? errorCode,
        string? lastError)
    {
        // Arrange
        var connection = CreateConnection(
            testStatus: status,
            errorCode: errorCode,
            lastError: lastError);

        // Act
        var result = connection.HasError;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasError_ReturnsFalse_WhenStatusIsSuccessfulEvenWithErrorDetails()
    {
        // Arrange
        var connection = CreateConnection(
            testStatus: "CONNECTED",
            errorCode: "STALE_ERROR",
            lastError: "stale error");

        // Act
        var result = connection.HasError;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsDisabled_IsInverseOfIsActive()
    {
        // Arrange
        var active = CreateConnection(isActive: true);
        var inactive = CreateConnection(isActive: false);

        // Act
        var activeResult = active.IsDisabled;
        var inactiveResult = inactive.IsDisabled;

        // Assert
        Assert.False(activeResult);
        Assert.True(inactiveResult);
    }

    private static ProviderConnection CreateConnection(
        bool isActive = true,
        string? testStatus = null,
        string? errorCode = null,
        string? lastError = null) =>
        new(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            isActive,
            TestStatus: testStatus,
            ErrorCode: errorCode,
            LastError: lastError);
}
