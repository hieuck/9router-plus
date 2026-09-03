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
}
