using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

public abstract class ExistingOAuthProviderAdapter : IProviderOAuthAdapter
{
    protected ExistingOAuthProviderAdapter(ProviderKind provider)
    {
        Provider = provider;
    }

    public ProviderKind Provider { get; }

    public abstract Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken);

    protected static void ValidateRequest(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        ProviderKind expectedProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(googleAuthentication);
        if (request.Provider != expectedProvider)
        {
            throw new ArgumentException(
                $"OAuth request provider must be {expectedProvider}.",
                nameof(request));
        }
    }

    protected static ProviderOAuthResult Map(OAuthConsentResult result) =>
        ProviderOAuthResult.FromConsent(result);
}

public sealed class CodexOAuthAdapter : ExistingOAuthProviderAdapter
{
    public CodexOAuthAdapter() : base(ProviderKind.Codex) { }

    public override async Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, googleAuthentication, ProviderKind.Codex);
        var automation = new CodexOAuthAutomation(
            request.CdpSession.Client,
            request.CdpSession.SessionId,
            request.CdpSession.TargetId,
            request.ProfileEmail);
        return Map(await automation.WaitAndConsentAsync(
            request.TargetServiceUri,
            request.Timeout,
            cancellationToken));
    }
}

public sealed class GitHubOAuthAdapter : ExistingOAuthProviderAdapter
{
    public GitHubOAuthAdapter() : base(ProviderKind.GitHub) { }

    public override async Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, googleAuthentication, ProviderKind.GitHub);
        var automation = new GitHubOAuthAutomation(
            request.CdpSession.Client,
            request.CdpSession.SessionId,
            request.CdpSession.TargetId,
            request.ProfileEmail);
        return Map(await automation.WaitAndConsentAsync(
            request.TargetServiceUri,
            request.Timeout,
            cancellationToken));
    }
}

public sealed class OpenRouterOAuthAdapter : ExistingOAuthProviderAdapter
{
    public OpenRouterOAuthAdapter() : base(ProviderKind.OpenRouter) { }

    public override async Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, googleAuthentication, ProviderKind.OpenRouter);
        var automation = new OpenRouterOAuthAutomation(
            request.CdpSession.Client,
            request.CdpSession.SessionId,
            request.CdpSession.TargetId,
            request.ProfileEmail);
        return Map(await automation.WaitAndConsentAsync(
            request.TargetServiceUri,
            request.Timeout,
            cancellationToken));
    }
}

public sealed class AwsBuilderIdOAuthAdapter : ExistingOAuthProviderAdapter
{
    public AwsBuilderIdOAuthAdapter() : base(ProviderKind.Kiro) { }

    public override async Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, googleAuthentication, ProviderKind.Kiro);
        var automation = new AwsBuilderIdOAuthAutomation(
            request.CdpSession.Client,
            request.CdpSession.SessionId,
            request.CdpSession.TargetId,
            request.ProfileEmail,
            request.TotpGenerator);
        return Map(await automation.WaitAndConsentAsync(
            request.TargetServiceUri,
            request.Timeout,
            cancellationToken));
    }
}
