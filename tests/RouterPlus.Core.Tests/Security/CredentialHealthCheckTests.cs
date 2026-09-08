using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests.Security;

public class CredentialHealthCheckTests
{
    [Fact]
    public void Healthy_CreatesHealthyResult()
    {
        // Act
        var result = CredentialHealthCheckResult.Healthy();

        // Assert
        Assert.Equal(CredentialHealthStatus.Healthy, result.Status);
        Assert.Equal("Credentials are valid", result.Message);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Healthy_WithCustomMessage_CreatesHealthyResultWithMessage()
    {
        // Arrange
        var customMessage = "Custom healthy message";

        // Act
        var result = CredentialHealthCheckResult.Healthy(customMessage);

        // Assert
        Assert.Equal(CredentialHealthStatus.Healthy, result.Status);
        Assert.Equal(customMessage, result.Message);
    }

    [Fact]
    public void Invalid_CreatesInvalidResult()
    {
        // Act
        var result = CredentialHealthCheckResult.Invalid();

        // Assert
        Assert.Equal(CredentialHealthStatus.Invalid, result.Status);
        Assert.Equal("Invalid credentials", result.Message);
    }

    [Fact]
    public void Invalid_WithCustomMessage_CreatesInvalidResultWithMessage()
    {
        // Arrange
        var customMessage = "Wrong password";

        // Act
        var result = CredentialHealthCheckResult.Invalid(customMessage);

        // Assert
        Assert.Equal(CredentialHealthStatus.Invalid, result.Status);
        Assert.Equal(customMessage, result.Message);
    }

    [Fact]
    public void RequiresAction_CreatesRequiresActionResult()
    {
        // Arrange
        var message = "CAPTCHA required";

        // Act
        var result = CredentialHealthCheckResult.RequiresAction(message);

        // Assert
        Assert.Equal(CredentialHealthStatus.RequiresAction, result.Status);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void Error_CreatesErrorResult()
    {
        // Arrange
        var message = "Network error";
        var exception = new Exception("Connection failed");

        // Act
        var result = CredentialHealthCheckResult.Error(message, exception);

        // Assert
        Assert.Equal(CredentialHealthStatus.Error, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    public void Remaining_factory_methods_create_results_with_expected_defaults()
    {
        var expired = CredentialHealthCheckResult.Expired();
        var unknown = CredentialHealthCheckResult.Unknown();
        var checking = CredentialHealthCheckResult.Checking();
        var notConfigured = CredentialHealthCheckResult.NotConfigured();

        Assert.Equal(CredentialHealthStatus.Expired, expired.Status);
        Assert.Equal("Credentials expired", expired.Message);
        Assert.Equal(CredentialHealthStatus.Unknown, unknown.Status);
        Assert.Equal("Health status unknown", unknown.Message);
        Assert.Equal(CredentialHealthStatus.Checking, checking.Status);
        Assert.Equal("Checking credentials...", checking.Message);
        Assert.Equal(CredentialHealthStatus.NotConfigured, notConfigured.Status);
        Assert.Equal("No credentials configured", notConfigured.Message);
        Assert.Null(expired.Exception);
        Assert.Null(unknown.Exception);
        Assert.Null(checking.Exception);
        Assert.Null(notConfigured.Exception);
    }

    [Fact]
    public void Status_extensions_use_enum_name_and_question_mark_for_unknown_values()
    {
        const CredentialHealthStatus undefinedStatus = (CredentialHealthStatus)999;

        Assert.Equal("999", undefinedStatus.ToDisplayText());
        Assert.Equal("?", undefinedStatus.ToEmoji());
    }

    [Fact]
    public void LastChecked_IsSetToCurrentTime()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var result = CredentialHealthCheckResult.Healthy();

        // Assert
        var after = DateTime.UtcNow;
        Assert.InRange(result.LastChecked, before, after);
    }

    [Theory]
    [InlineData(CredentialHealthStatus.Healthy, true)]
    [InlineData(CredentialHealthStatus.Invalid, false)]
    [InlineData(CredentialHealthStatus.Expired, false)]
    [InlineData(CredentialHealthStatus.RequiresAction, false)]
    [InlineData(CredentialHealthStatus.Error, false)]
    [InlineData(CredentialHealthStatus.Unknown, false)]
    [InlineData(CredentialHealthStatus.Checking, false)]
    [InlineData(CredentialHealthStatus.NotConfigured, false)]
    public void IsHealthy_ReturnsCorrectValue(CredentialHealthStatus status, bool expected)
    {
        // Act
        var result = status.IsHealthy();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(CredentialHealthStatus.Invalid, true)]
    [InlineData(CredentialHealthStatus.Expired, true)]
    [InlineData(CredentialHealthStatus.RequiresAction, true)]
    [InlineData(CredentialHealthStatus.Error, true)]
    [InlineData(CredentialHealthStatus.Healthy, false)]
    [InlineData(CredentialHealthStatus.Unknown, false)]
    [InlineData(CredentialHealthStatus.Checking, false)]
    [InlineData(CredentialHealthStatus.NotConfigured, false)]
    public void NeedsAttention_ReturnsCorrectValue(CredentialHealthStatus status, bool expected)
    {
        // Act
        var result = status.NeedsAttention();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(CredentialHealthStatus.Unknown, "Unknown")]
    [InlineData(CredentialHealthStatus.Checking, "Checking...")]
    [InlineData(CredentialHealthStatus.Healthy, "✓ Healthy")]
    [InlineData(CredentialHealthStatus.Invalid, "✗ Invalid")]
    [InlineData(CredentialHealthStatus.Expired, "⚠ Expired")]
    [InlineData(CredentialHealthStatus.RequiresAction, "⚠ Action Required")]
    [InlineData(CredentialHealthStatus.NotConfigured, "Not Configured")]
    [InlineData(CredentialHealthStatus.Error, "✗ Error")]
    public void ToDisplayText_ReturnsCorrectText(CredentialHealthStatus status, string expected)
    {
        // Act
        var result = status.ToDisplayText();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(CredentialHealthStatus.Healthy, "✓")]
    [InlineData(CredentialHealthStatus.Invalid, "✗")]
    [InlineData(CredentialHealthStatus.Expired, "⚠")]
    [InlineData(CredentialHealthStatus.RequiresAction, "⚠")]
    [InlineData(CredentialHealthStatus.Checking, "⟳")]
    [InlineData(CredentialHealthStatus.NotConfigured, "○")]
    [InlineData(CredentialHealthStatus.Error, "✗")]
    [InlineData(CredentialHealthStatus.Unknown, "?")]
    public void ToEmoji_ReturnsCorrectEmoji(CredentialHealthStatus status, string expected)
    {
        // Act
        var result = status.ToEmoji();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(CredentialHealthStatus.Expired, "Credentials expired")]
    [InlineData(CredentialHealthStatus.Unknown, "Health status unknown")]
    [InlineData(CredentialHealthStatus.Checking, "Checking credentials...")]
    [InlineData(CredentialHealthStatus.NotConfigured, "No credentials configured")]
    public void Factory_methods_create_results_for_remaining_statuses(
        CredentialHealthStatus expectedStatus,
        string expectedMessage)
    {
        // Act
        var result = expectedStatus switch
        {
            CredentialHealthStatus.Expired => CredentialHealthCheckResult.Expired(),
            CredentialHealthStatus.Unknown => CredentialHealthCheckResult.Unknown(),
            CredentialHealthStatus.Checking => CredentialHealthCheckResult.Checking(),
            CredentialHealthStatus.NotConfigured => CredentialHealthCheckResult.NotConfigured(),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedStatus))
        };

        // Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Error_without_exception_keeps_exception_null()
    {
        // Act
        var result = CredentialHealthCheckResult.Error("Synthetic error");

        // Assert
        Assert.Equal(CredentialHealthStatus.Error, result.Status);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Unknown_status_uses_enum_name_fallback_for_display_text()
    {
        // Arrange
        const CredentialHealthStatus unknownStatus = (CredentialHealthStatus)999;

        // Act
        var displayText = unknownStatus.ToDisplayText();
        var emoji = unknownStatus.ToEmoji();

        // Assert
        Assert.Equal("999", displayText);
        Assert.Equal("?", emoji);
    }
}
