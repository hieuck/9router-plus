using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests.Security;

public sealed class CredentialHealthCheckResultFactoryTests
{
    [Theory]
    [InlineData(CredentialHealthStatus.Healthy, "Credentials are valid")]
    [InlineData(CredentialHealthStatus.Invalid, "Invalid credentials")]
    [InlineData(CredentialHealthStatus.Expired, "Credentials expired")]
    [InlineData(CredentialHealthStatus.Unknown, "Health status unknown")]
    [InlineData(CredentialHealthStatus.Checking, "Checking credentials...")]
    [InlineData(CredentialHealthStatus.NotConfigured, "No credentials configured")]
    public void FactoryWithDefaultMessage_ReturnsExpectedStatusAndMessage(
        CredentialHealthStatus expectedStatus,
        string expectedMessage)
    {
        // Arrange
        Func<CredentialHealthCheckResult> factory = expectedStatus switch
        {
            CredentialHealthStatus.Healthy => () => CredentialHealthCheckResult.Healthy(),
            CredentialHealthStatus.Invalid => () => CredentialHealthCheckResult.Invalid(),
            CredentialHealthStatus.Expired => () => CredentialHealthCheckResult.Expired(),
            CredentialHealthStatus.Unknown => () => CredentialHealthCheckResult.Unknown(),
            CredentialHealthStatus.Checking => () => CredentialHealthCheckResult.Checking(),
            CredentialHealthStatus.NotConfigured => () => CredentialHealthCheckResult.NotConfigured(),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedStatus), expectedStatus, null)
        };

        // Act
        var result = factory();

        // Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void RequiresAction_WithSyntheticMessage_ReturnsActionRequiredResult()
    {
        // Arrange
        const string message = "Synthetic action required";

        // Act
        var result = CredentialHealthCheckResult.RequiresAction(message);

        // Assert
        Assert.Equal(CredentialHealthStatus.RequiresAction, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Error_PreservesSyntheticException()
    {
        // Arrange
        const string message = "Synthetic system error";
        var exception = new InvalidOperationException("Synthetic exception");

        // Act
        var result = CredentialHealthCheckResult.Error(message, exception);

        // Assert
        Assert.Equal(CredentialHealthStatus.Error, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Same(exception, result.Exception);
    }
}
