using RouterPlus.Core.Security;

namespace RouterPlus.Core.Tests.Security;

public sealed class GoogleLoginResultTests
{
    [Theory]
    [InlineData(GoogleLoginResultCategory.Success, "Login completed successfully.")]
    [InlineData(GoogleLoginResultCategory.InvalidCredentials, "Invalid email, password, or TOTP code.")]
    [InlineData(GoogleLoginResultCategory.Timeout, "Login automation timed out.")]
    [InlineData(GoogleLoginResultCategory.Cancelled, "Login automation was cancelled.")]
    public void Factory_creates_expected_terminal_result(
        GoogleLoginResultCategory expectedCategory,
        string expectedMessage)
    {
        // Arrange
        var result = expectedCategory switch
        {
            GoogleLoginResultCategory.Success => GoogleLoginResult.Success(),
            GoogleLoginResultCategory.InvalidCredentials => GoogleLoginResult.InvalidCredentials(),
            GoogleLoginResultCategory.Timeout => GoogleLoginResult.Timeout(),
            GoogleLoginResultCategory.Cancelled => GoogleLoginResult.Cancelled(),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedCategory))
        };

        // Act
        var category = result.Category;
        var message = result.Message;

        // Assert
        Assert.Equal(expectedCategory, category);
        Assert.Equal(expectedMessage, message);
    }

    [Fact]
    public void ManualInterventionRequired_preserves_reason()
    {
        // Arrange
        const string reason = "Complete the security challenge in the browser.";

        // Act
        var result = GoogleLoginResult.ManualInterventionRequired(reason);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.ManualInterventionRequired, result.Category);
        Assert.Equal(reason, result.Message);
    }

    [Fact]
    public void BrowserDisconnected_without_message_uses_safe_default()
    {
        // Arrange
        const string expectedMessage = "Browser connection was lost.";

        // Act
        var result = GoogleLoginResult.BrowserDisconnected();

        // Assert
        Assert.Equal(GoogleLoginResultCategory.BrowserDisconnected, result.Category);
        Assert.Equal(expectedMessage, result.Message);
    }

    [Fact]
    public void BrowserDisconnected_with_message_preserves_message()
    {
        // Arrange
        const string message = "The browser process exited.";

        // Act
        var result = GoogleLoginResult.BrowserDisconnected(message);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.BrowserDisconnected, result.Category);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void BrowserDisconnected_rejects_blank_message()
    {
        // Arrange
        const string message = " ";

        // Act
        var exception = Assert.Throws<ArgumentException>(() => GoogleLoginResult.BrowserDisconnected(message));

        // Assert
        Assert.Equal("message", exception.ParamName);
    }

    [Fact]
    public void UnsupportedPage_preserves_reason()
    {
        // Arrange
        const string reason = "The page did not expose a recognized login state.";

        // Act
        var result = GoogleLoginResult.UnsupportedPage(reason);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Equal(reason, result.Message);
    }
}
