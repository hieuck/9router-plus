using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Services;

public interface IDirectLoginAutomation
{
    Task<DirectLoginResult> RunAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IDirectLoginAutomationFactory
{
    IDirectLoginAutomation Create(
        ProviderKind provider,
        CdpSession cdpSession,
        ProviderCredential credential,
        Func<Task<string?>>? totpGenerator);
}

internal sealed class DefaultDirectLoginAutomationFactory : IDirectLoginAutomationFactory
{
    public IDirectLoginAutomation Create(
        ProviderKind provider,
        CdpSession cdpSession,
        ProviderCredential credential,
        Func<Task<string?>>? totpGenerator)
    {
        DirectLoginAutomation automation = provider switch
        {
            ProviderKind.GitHub => new GitHubDirectLoginAutomation(
                cdpSession.Client, cdpSession.SessionId, cdpSession.TargetId,
                credential.Email, credential.Password, totpGenerator),
            ProviderKind.OpenRouter => new OpenRouterDirectLoginAutomation(
                cdpSession.Client, cdpSession.SessionId, cdpSession.TargetId,
                credential.Email, credential.Password, totpGenerator),
            ProviderKind.Codex => new CodexDirectLoginAutomation(
                cdpSession.Client, cdpSession.SessionId, cdpSession.TargetId,
                credential.Email, credential.Password, totpGenerator),
            ProviderKind.Kiro => new KiroDirectLoginAutomation(
                cdpSession.Client, cdpSession.SessionId, cdpSession.TargetId,
                credential.Email, credential.Password, totpGenerator),
            _ => throw new NotSupportedException($"Direct login not supported for provider {provider}")
        };

        return new DirectLoginAutomationAdapter(automation);
    }

    private sealed class DirectLoginAutomationAdapter(DirectLoginAutomation automation) : IDirectLoginAutomation
    {
        public Task<DirectLoginResult> RunAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            automation.RunAsync(timeout, cancellationToken);
    }
}
