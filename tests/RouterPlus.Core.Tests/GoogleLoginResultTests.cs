using RouterPlus.Core.Security;

namespace RouterPlus.Core.Tests;

public sealed class GoogleLoginResultTests
{
    [Theory]
    [InlineData(GoogleLoginResultCategory.Success, "Login completed successfully.")]
    [InlineData(GoogleLoginResultCategory.InvalidCredentials, "Invalid email, password, or TOTP code.")]
    [InlineData(GoogleLoginResultCategory.Timeout, "Login automation timed out.")]
    [InlineData(GoogleLoginResultCategory.Cancelled, "Login automation was cancelled.")]
    [InlineData(GoogleLoginResultCategory.BrowserDisconnected, "Browser connection was lost.")]
    public void Factory_methods_create_expected_result(
        GoogleLoginResultCategory expectedCategory,
        string expectedMessage)
    {
        // Act
        var result = expectedCategory switch
        {
            GoogleLoginResultCategory.Success => GoogleLoginResult.Success(),
            GoogleLoginResultCategory.InvalidCredentials => GoogleLoginResult.InvalidCredentials(),
            GoogleLoginResultCategory.Timeout => GoogleLoginResult.Timeout(),
            GoogleLoginResultCategory.Cancelled => GoogleLoginResult.Cancelled(),
            GoogleLoginResultCategory.BrowserDisconnected => GoogleLoginResult.BrowserDisconnected(),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedCategory))
        };

        // Assert
        Assert.Equal(expectedCategory, result.Category);
        Assert.Equal(expectedMessage, result.Message);
    }

    [Fact]
    public void Factory_methods_preserve_custom_messages()
    {
        // Arrange
        const string reason = "Synthetic challenge requires operator review.";

        // Act
        var manual = GoogleLoginResult.ManualInterventionRequired(reason);
        var unsupported = GoogleLoginResult.UnsupportedPage(reason);
        var disconnected = GoogleLoginResult.BrowserDisconnected(reason);

        // Assert
        Assert.Equal(reason, manual.Message);
        Assert.Equal(reason, unsupported.Message);
        Assert.Equal(reason, disconnected.Message);
    }

    [Fact]
    public void BrowserDisconnected_rejects_blank_custom_message()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => GoogleLoginResult.BrowserDisconnected(" "));
    }
}
