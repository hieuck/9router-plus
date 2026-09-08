using System.Reflection;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleOAuthAutomationBranchTests
{
    [Theory]
    [InlineData("accounts.google.com", true)]
    [InlineData("ACCOUNTS.GOOGLE.COM.", true)]
    [InlineData("accounts.google.com.evil.test", false)]
    [InlineData("google.com", false)]
    public void IsGoogleOAuthHost_accepts_only_accounts_google_origins(string host, bool expected)
    {
        // Arrange

        // Act
        var result = GoogleOAuthPageDetector.IsGoogleOAuthHost(host);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task TryDetectAsync_returns_null_when_cdp_read_fails()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();

        // Act
        var result = await GoogleOAuthPageDetector.TryDetectAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Detector_actions_return_false_when_cdp_click_or_fill_fails()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();

        // Act
        var accountResult = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client,
            "synthetic-session",
            "user@example.com",
            CancellationToken.None);
        var totpResult = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client,
            "synthetic-session",
            "123456",
            CancellationToken.None);
        var consentResult = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.False(accountResult);
        Assert.False(totpResult);
        Assert.False(consentResult);
    }

    [Fact]
    public async Task OpenRouter_provider_actions_return_false_when_cdp_click_fails()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();
        var automation = new OpenRouterOAuthAutomation(
            client,
            "synthetic-session",
            "synthetic-target",
            "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new OpenRouterOAuthPageState
            {
                CurrentUrl = "https://openrouter.ai/login",
                IsOpenRouterOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = true,
                HasTermsConsentButton = true
            }
        };

        // Act
        var initialResult = await InvokeAsync<bool>(
            automation,
            "TryClickProviderInitialButtonAsync",
            state,
            CancellationToken.None);
        var consentResult = await InvokeAsync<bool>(
            automation,
            "TryClickProviderConsentButtonAsync",
            state,
            CancellationToken.None);

        // Assert
        Assert.False(initialResult);
        Assert.False(consentResult);
    }

    [Fact]
    public async Task AwsBuilderId_provider_actions_return_false_when_cdp_click_fails()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();
        var automation = new AwsBuilderIdOAuthAutomation(
            client,
            "synthetic-session",
            "synthetic-target",
            "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = "https://auth.kiro.dev/",
                IsAwsBuilderIdPage = true,
                IsCompletionPage = false,
                HasContinueWithGoogleButton = true,
                HasAwsConsentButton = true
            }
        };

        // Act
        var initialResult = await InvokeAsync<bool>(
            automation,
            "TryClickProviderInitialButtonAsync",
            state,
            CancellationToken.None);
        var consentResult = await InvokeAsync<bool>(
            automation,
            "TryClickProviderConsentButtonAsync",
            state,
            CancellationToken.None);

        // Assert
        Assert.False(initialResult);
        Assert.False(consentResult);
    }

    [Fact]
    public async Task Kiro_login_completion_returns_false_when_cdp_read_fails()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();
        var automation = new KiroDirectLoginAutomation(
            client,
            "synthetic-session",
            "synthetic-target",
            "user@example.com",
            "synthetic-password");

        // Act
        var result = await InvokeAsync<bool>(
            automation,
            "IsLoginCompleteAsync",
            CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task OpenRouter_completion_is_incomplete_for_missing_provider_state()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();
        var automation = new OpenRouterOAuthAutomation(
            client,
            "synthetic-session",
            "synthetic-target",
            "user@example.com");

        // Act
        var result = InvokeCompletionCheck(
            automation,
            new GoogleOAuthFlowAutomation.CombinedOAuthPageState());

        // Assert
        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task AwsBuilderId_completion_is_incomplete_when_authorization_has_not_finished()
    {
        // Arrange
        await using var client = CreateUnconnectedClient();
        var automation = new AwsBuilderIdOAuthAutomation(
            client,
            "synthetic-session",
            "synthetic-target",
            "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = "https://auth.kiro.dev/",
                IsAwsBuilderIdPage = true,
                IsCompletionPage = false,
                HasContinueWithGoogleButton = false,
                HasAwsConsentButton = false
            }
        };

        // Act
        var result = InvokeCompletionCheck(automation, state);

        // Assert
        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    private static ChromeCdpClient CreateUnconnectedClient() =>
        new(new Uri("http://127.0.0.1:9222"));

    private static GoogleOAuthFlowAutomation.CompletionCheckResult InvokeCompletionCheck(
        GoogleOAuthFlowAutomation automation,
        GoogleOAuthFlowAutomation.CombinedOAuthPageState state) =>
        (GoogleOAuthFlowAutomation.CompletionCheckResult)automation.GetType()
            .BaseType!
            .GetMethod("CheckCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(automation, [state])!;

    private static async Task<T> InvokeAsync<T>(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<T>)method.Invoke(target, args)!;
        return await task;
    }
}
