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
    [InlineData(0.01)]
    [InlineData(49.99)]
    [InlineData(79.99)]
    public void IsNearLimit_ReturnsFalse_WhenJustBelowThreshold(decimal percentage)
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: (long)percentage,
            LimitCount: 100);

        Assert.False(connection.IsNearLimit);
    }

    [Theory]
    [InlineData(80.0)]
    [InlineData(85.5)]
    [InlineData(99.9)]
    public void IsNearLimit_ReturnsTrue_WhenAtOrAboveThreshold(decimal percentage)
    {
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: (long)percentage,
            LimitCount: 100);

        Assert.True(connection.IsNearLimit);
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

        // Vietnamese culture uses comma as decimal separator
        Assert.Equal("49,54 / 50", quota.UsageText);
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

        // Vietnamese culture uses comma as decimal separator
        Assert.Equal("75,57%", quota.PercentageText);
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsDisabled_is_the_inverse_of_active_state(bool isActive, bool expected)
    {
        // Arrange
        var connection = new ProviderConnection("conn-1", ProviderKind.Codex, "Test", 1, isActive);

        // Act
        var actual = connection.IsDisabled;

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsagePercentage_falls_back_to_legacy_counts_when_quota_rows_have_no_percentage()
    {
        // Arrange
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 25,
            LimitCount: 100,
            Quotas: [new ProviderQuota("requests", null, null, null, null)]);

        // Act
        var percentage = connection.UsagePercentage;

        // Assert
        Assert.Equal(25.0, percentage);
    }

    [Fact]
    public void UsagePercentage_uses_the_first_quota_with_a_percentage()
    {
        // Arrange
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            UsageCount: 25,
            LimitCount: 100,
            Quotas:
            [
                new ProviderQuota("unavailable", null, null, null, null),
                new ProviderQuota("requests", 40m, 80m, 40m, null),
                new ProviderQuota("other", 90m, 100m, 10m, null)
            ]);

        // Act
        var percentage = connection.UsagePercentage;

        // Assert
        Assert.Equal(50.0, percentage);
    }

    [Theory]
    [InlineData(" active ")]
    [InlineData("OK")]
    [InlineData("healthy")]
    [InlineData("available")]
    [InlineData("ready")]
    [InlineData("success")]
    [InlineData("connected")]
    public void HasSuccessfulTestStatus_accepts_success_statuses_case_insensitively(string status)
    {
        // Arrange
        var connection = new ProviderConnection("conn-1", ProviderKind.Codex, "Test", 1, true, TestStatus: status);

        // Act
        var isSuccessful = connection.HasSuccessfulTestStatus;

        // Assert
        Assert.True(isSuccessful);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pending")]
    public void HasUnknownTestStatus_identifies_missing_or_unrecognized_status(string? status)
    {
        // Arrange
        var connection = new ProviderConnection("conn-1", ProviderKind.Codex, "Test", 1, true, TestStatus: status);

        // Act
        var isUnknown = connection.HasUnknownTestStatus;

        // Assert
        Assert.True(isUnknown);
    }

    [Fact]
    public void HasUnknownTestStatus_is_false_for_success_and_error_statuses()
    {
        // Arrange
        var successful = new ProviderConnection("conn-1", ProviderKind.Codex, "Test", 1, true, TestStatus: "ready");
        var errored = new ProviderConnection("conn-2", ProviderKind.Codex, "Test", 1, true, TestStatus: "failed");

        // Act
        var successfulUnknown = successful.HasUnknownTestStatus;
        var erroredUnknown = errored.HasUnknownTestStatus;

        // Assert
        Assert.False(successfulUnknown);
        Assert.False(erroredUnknown);
    }

    [Theory]
    [InlineData("401", null, null)]
    [InlineData(null, "request failed", null)]
    [InlineData(null, null, "expired")]
    public void HasError_detects_error_code_message_or_error_status(
        string? errorCode,
        string? lastError,
        string? testStatus)
    {
        // Arrange
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            TestStatus: testStatus,
            ErrorCode: errorCode,
            LastError: lastError);

        // Act
        var hasError = connection.HasError;

        // Assert
        Assert.True(hasError);
    }

    [Fact]
    public void HasError_is_false_when_success_status_overrides_stale_error_metadata()
    {
        // Arrange
        var connection = new ProviderConnection(
            "conn-1",
            ProviderKind.Codex,
            "Test",
            1,
            true,
            TestStatus: "connected",
            ErrorCode: "401",
            LastError: "stale error");

        // Act
        var hasError = connection.HasError;

        // Assert
        Assert.False(hasError);
    }

    [Theory]
    [InlineData(79.99, false)]
    [InlineData(80.0, true)]
    public void ProviderQuota_IsNearLimit_uses_the_eighty_percent_threshold(decimal used, bool expected)
    {
        // Arrange
        var quota = new ProviderQuota("requests", used, 100m, 100m - used, null);

        // Act
        var isNearLimit = quota.IsNearLimit;

        // Assert
        Assert.Equal(expected, isNearLimit);
    }

    [Theory]
    [InlineData(99.99, false)]
    [InlineData(100.0, true)]
    public void ProviderQuota_IsOverLimit_uses_the_hundred_percent_threshold(decimal used, bool expected)
    {
        // Arrange
        var quota = new ProviderQuota("requests", used, 100m, 100m - used, null);

        // Act
        var isOverLimit = quota.IsOverLimit;

        // Assert
        Assert.Equal(expected, isOverLimit);
    }

    [Fact]
    public void ProviderQuota_UsageText_returns_placeholder_when_used_is_missing()
    {
        // Arrange
        var quota = new ProviderQuota("requests", null, 100m, null, null);

        // Act
        var usageText = quota.UsageText;

        // Assert
        Assert.Equal("Chưa có dữ liệu", usageText);
    }

    [Fact]
    public void ProviderQuota_UsageText_returns_placeholder_when_total_is_missing()
    {
        // Arrange
        var quota = new ProviderQuota("requests", 100m, null, null, null);

        // Act
        var usageText = quota.UsageText;

        // Assert
        Assert.Equal("Chưa có dữ liệu", usageText);
    }
}
