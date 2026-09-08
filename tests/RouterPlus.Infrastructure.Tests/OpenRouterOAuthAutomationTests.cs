using System.Reflection;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OpenRouterOAuthAutomationTests
{
    private static readonly Uri OpenRouterLoginUri = new("https://openrouter.ai/login");

    [Fact]
    public void CheckCompletion_returnsAlreadyAuthorized_whenOnOpenRouterTargetPage()
    {
        // Arrange
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = CombinedState(new OpenRouterOAuthPageState
        {
            CurrentUrl = "https://openrouter.ai/workspaces/default/keys",
            IsOpenRouterOAuthPage = false,
            IsTargetService = true,
            HasGoogleLoginButton = false,
            HasTermsConsentButton = false
        });

        // Act
        var result = Invoke<GoogleOAuthFlowAutomation.CompletionCheckResult>(
            automation, "CheckCompletion", state);

        // Assert
        Assert.True(result.IsComplete);
        Assert.NotNull(result.Result);
        Assert.True(result.Result!.Success);
        Assert.True(result.Result.AlreadyAuthorized);
        Assert.Equal("Already authorized - on OpenRouter", result.Result.Message);
    }

    [Fact]
    public void CheckCompletion_returnsIncomplete_whenOpenRouterOAuthPageIsStillActive()
    {
        // Arrange
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = CombinedState(new OpenRouterOAuthPageState
        {
            CurrentUrl = OpenRouterLoginUri.ToString(),
            IsOpenRouterOAuthPage = true,
            IsTargetService = false,
            HasGoogleLoginButton = true,
            HasTermsConsentButton = false
        });

        // Act
        var result = Invoke<GoogleOAuthFlowAutomation.CompletionCheckResult>(
            automation, "CheckCompletion", state);

        // Assert
        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    [Fact]
    public void CheckCompletion_returnsIncomplete_whenProviderStateIsMissing()
    {
        // Arrange
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState();

        // Act
        var result = Invoke<GoogleOAuthFlowAutomation.CompletionCheckResult>(
            automation, "CheckCompletion", state);

        // Assert
        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    [Fact]
    public void ShouldClickProviderInitialButton_onlyOnNonGooglePageWithGoogleLogin()
    {
        // Arrange
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var providerState = new OpenRouterOAuthPageState
        {
            CurrentUrl = OpenRouterLoginUri.ToString(),
            IsOpenRouterOAuthPage = true,
            IsTargetService = false,
            HasGoogleLoginButton = true,
            HasTermsConsentButton = false
        };

        // Act
        var providerPage = Invoke<bool>(automation, "ShouldClickProviderInitialButton", CombinedState(providerState));
        var googlePage = Invoke<bool>(automation, "ShouldClickProviderInitialButton", CombinedState(providerState, google: new GoogleOAuthPageState
        {
            CurrentUrl = "https://accounts.google.com/signin",
            HasAccountPicker = false,
            HasGoogleTotpInput = false,
            HasGoogleConsentButton = false
        }));
        var noButton = Invoke<bool>(automation, "ShouldClickProviderInitialButton", CombinedState(providerState with { HasGoogleLoginButton = false }));
        var missingProvider = Invoke<bool>(automation, "ShouldClickProviderInitialButton", new GoogleOAuthFlowAutomation.CombinedOAuthPageState());

        // Assert
        Assert.True(providerPage);
        Assert.False(googlePage);
        Assert.False(noButton);
        Assert.False(missingProvider);
    }

    [Fact]
    public void ShouldClickProviderConsent_onlyOnNonGooglePageWithTermsButton()
    {
        // Arrange
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var providerState = new OpenRouterOAuthPageState
        {
            CurrentUrl = "https://openrouter.ai/oauth/callback",
            IsOpenRouterOAuthPage = true,
            IsTargetService = false,
            HasGoogleLoginButton = false,
            HasTermsConsentButton = true
        };

        // Act
        var providerPage = Invoke<bool>(automation, "ShouldClickProviderConsent", CombinedState(providerState));
        var googlePage = Invoke<bool>(automation, "ShouldClickProviderConsent", CombinedState(providerState, google: new GoogleOAuthPageState
        {
            CurrentUrl = "https://accounts.google.com/o/oauth2/consent",
            HasAccountPicker = false,
            HasGoogleTotpInput = false,
            HasGoogleConsentButton = true
        }));
        var noButton = Invoke<bool>(automation, "ShouldClickProviderConsent", CombinedState(providerState with { HasTermsConsentButton = false }));
        var missingProvider = Invoke<bool>(automation, "ShouldClickProviderConsent", new GoogleOAuthFlowAutomation.CombinedOAuthPageState());

        // Assert
        Assert.True(providerPage);
        Assert.False(googlePage);
        Assert.False(noButton);
        Assert.False(missingProvider);
    }

    [Fact]
    public async Task TryClickProviderButtons_returnFalse_whenProviderStateDoesNotMatch()
    {
        // Arrange
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));
        var automation = new OpenRouterOAuthAutomation(client, "session", "target", "user@example.com");
        var noButton = CombinedState(new OpenRouterOAuthPageState
        {
            CurrentUrl = OpenRouterLoginUri.ToString(),
            IsOpenRouterOAuthPage = true,
            IsTargetService = false,
            HasGoogleLoginButton = false,
            HasTermsConsentButton = false
        });
        var missingProvider = new GoogleOAuthFlowAutomation.CombinedOAuthPageState();

        // Act
        var initialWithoutButton = await InvokeAsync<bool>(automation, "TryClickProviderInitialButtonAsync", noButton, CancellationToken.None);
        var consentWithoutButton = await InvokeAsync<bool>(automation, "TryClickProviderConsentButtonAsync", noButton, CancellationToken.None);
        var initialWithoutProvider = await InvokeAsync<bool>(automation, "TryClickProviderInitialButtonAsync", missingProvider, CancellationToken.None);
        var consentWithoutProvider = await InvokeAsync<bool>(automation, "TryClickProviderConsentButtonAsync", missingProvider, CancellationToken.None);

        // Assert
        Assert.False(initialWithoutButton);
        Assert.False(consentWithoutButton);
        Assert.False(initialWithoutProvider);
        Assert.False(consentWithoutProvider);
    }

    private static GoogleOAuthFlowAutomation.CombinedOAuthPageState CombinedState(
        OpenRouterOAuthPageState provider,
        GoogleOAuthPageState? google = null) =>
        new()
        {
            ProviderState = provider,
            GoogleState = google
        };

    private static T Invoke<T>(OpenRouterOAuthAutomation automation, string methodName, params object?[] args)
    {
        var method = typeof(OpenRouterOAuthAutomation).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing method {methodName}");
        return (T)(method.Invoke(automation, args) ?? throw new InvalidOperationException($"Method {methodName} returned null"));
    }

    private static async Task<T> InvokeAsync<T>(OpenRouterOAuthAutomation automation, string methodName, params object?[] args)
    {
        var task = Invoke<Task<T>>(automation, methodName, args);
        return await task;
    }
}
