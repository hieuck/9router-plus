using System.Reflection;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OAuthAutomationDecisionTests
{
    [Fact]
    public async Task OpenRouter_CheckCompletion_returns_already_authorized_when_on_target_service()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new OpenRouterOAuthPageState
            {
                CurrentUrl = "https://openrouter.ai/workspaces/default/keys",
                IsOpenRouterOAuthPage = false,
                IsTargetService = true,
                HasGoogleLoginButton = false,
                HasTermsConsentButton = false
            }
        };

        // Act
        var result = InvokeCompletionCheck(automation, state);

        // Assert
        Assert.True(result.IsComplete);
        Assert.NotNull(result.Result);
        Assert.True(result.Result!.Success);
        Assert.True(result.Result.AlreadyAuthorized);
        Assert.Contains("Already authorized", result.Result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRouter_CheckCompletion_returns_incomplete_on_auth_page()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new OpenRouterOAuthPageState
            {
                CurrentUrl = "https://openrouter.ai/auth",
                IsOpenRouterOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = true,
                HasTermsConsentButton = false
            }
        };

        // Act
        var result = InvokeCompletionCheck(automation, state);

        // Assert
        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task OpenRouter_ShouldClickProviderInitialButton_requires_provider_google_login_button()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var providerState = new OpenRouterOAuthPageState
        {
            CurrentUrl = "https://openrouter.ai/login",
            IsOpenRouterOAuthPage = true,
            IsTargetService = false,
            HasGoogleLoginButton = true,
            HasTermsConsentButton = false
        };
        var providerPage = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = providerState
        };
        var googlePage = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = providerState,
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/signin",
                HasAccountPicker = false,
                HasGoogleTotpInput = false,
                HasGoogleConsentButton = false
            }
        };

        // Act
        var providerResult = InvokeDecision(automation, "ShouldClickProviderInitialButton", providerPage);
        var googleResult = InvokeDecision(automation, "ShouldClickProviderInitialButton", googlePage);

        // Assert
        Assert.True(providerResult);
        Assert.False(googleResult);
    }

    [Fact]
    public async Task OpenRouter_ShouldClickProviderConsent_returns_false_when_terms_button_is_missing()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new OpenRouterOAuthPageState
            {
                CurrentUrl = "https://openrouter.ai/terms",
                IsOpenRouterOAuthPage = false,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasTermsConsentButton = false
            }
        };

        // Act
        var result = InvokeDecision(automation, "ShouldClickProviderConsent", state);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task OpenRouter_ShouldClickProviderConsent_returns_false_on_google_owned_page()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new OpenRouterOAuthPageState
            {
                CurrentUrl = "https://openrouter.ai/terms",
                IsOpenRouterOAuthPage = false,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasTermsConsentButton = true
            },
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/signin",
                HasAccountPicker = false,
                HasGoogleTotpInput = false,
                HasGoogleConsentButton = false
            }
        };

        // Act
        var result = InvokeDecision(automation, "ShouldClickProviderConsent", state);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task OpenRouter_ShouldClickProviderConsent_returns_true_when_provider_terms_button_is_present()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new OpenRouterOAuthPageState
            {
                CurrentUrl = "https://openrouter.ai/terms",
                IsOpenRouterOAuthPage = false,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasTermsConsentButton = true
            }
        };

        // Act
        var result = InvokeDecision(automation, "ShouldClickProviderConsent", state);

        // Assert
        Assert.True(result);
    }


    [Fact]
    public async Task AwsBuilderId_CheckCompletion_returns_success_when_authorization_is_complete()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new AwsBuilderIdOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = "https://view.awsapps.com/start",
                IsAwsBuilderIdPage = true,
                IsCompletionPage = true,
                HasContinueWithGoogleButton = false,
                HasAwsConsentButton = false
            }
        };

        // Act
        var result = InvokeCompletionCheck(automation, state);

        // Assert
        Assert.True(result.IsComplete);
        Assert.NotNull(result.Result);
        Assert.True(result.Result!.Success);
        Assert.False(result.Result.AlreadyAuthorized);
        Assert.Contains("AWS Builder ID", result.Result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwsBuilderId_provider_decisions_require_matching_page_and_button()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new AwsBuilderIdOAuthAutomation(client, "session", "target", "user@example.com");
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
        var initialResult = InvokeDecision(automation, "ShouldClickProviderInitialButton", state);
        var consentResult = InvokeDecision(automation, "ShouldClickProviderConsent", state);

        // Assert
        Assert.True(initialResult);
        Assert.True(consentResult);
    }

    [Fact]
    public async Task AwsBuilderId_ShouldClickProviderInitialButton_returns_false_when_button_is_missing()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new AwsBuilderIdOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = "https://auth.kiro.dev/",
                IsAwsBuilderIdPage = true,
                IsCompletionPage = false,
                HasContinueWithGoogleButton = false,
                HasAwsConsentButton = true
            }
        };

        // Act
        var result = InvokeDecision(automation, "ShouldClickProviderInitialButton", state);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AwsBuilderId_ShouldClickProviderConsent_returns_false_when_button_is_missing()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new AwsBuilderIdOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = "https://auth.kiro.dev/",
                IsAwsBuilderIdPage = true,
                IsCompletionPage = false,
                HasContinueWithGoogleButton = true,
                HasAwsConsentButton = false
            }
        };

        // Act
        var result = InvokeDecision(automation, "ShouldClickProviderConsent", state);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AwsBuilderId_provider_decisions_return_false_on_non_aws_page()
    {
        // Arrange
        await using var client = CreateClient();
        var automation = new AwsBuilderIdOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = "https://example.com/",
                IsAwsBuilderIdPage = false,
                IsCompletionPage = false,
                HasContinueWithGoogleButton = true,
                HasAwsConsentButton = true
            }
        };

        // Act
        var initialResult = InvokeDecision(automation, "ShouldClickProviderInitialButton", state);
        var consentResult = InvokeDecision(automation, "ShouldClickProviderConsent", state);

        // Assert
        Assert.False(initialResult);
        Assert.False(consentResult);
    }

    private static ChromeCdpClient CreateClient() =>
        new(new Uri("http://127.0.0.1:9222"));

    private static GoogleOAuthFlowAutomation.CompletionCheckResult InvokeCompletionCheck(
        GoogleOAuthFlowAutomation automation,
        GoogleOAuthFlowAutomation.CombinedOAuthPageState state) =>
        (GoogleOAuthFlowAutomation.CompletionCheckResult)typeof(GoogleOAuthFlowAutomation)
            .GetMethod("CheckCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(automation, [state])!;

    private static bool InvokeDecision(
        GoogleOAuthFlowAutomation automation,
        string methodName,
        GoogleOAuthFlowAutomation.CombinedOAuthPageState state) =>
        (bool)automation.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(automation, [state])!;
}
