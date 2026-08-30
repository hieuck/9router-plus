using RouterPlus.Core.Providers;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Deterministic provider-to-OAuth-adapter routing. Unsupported providers fail
/// explicitly instead of falling back to another provider's automation.
/// </summary>
public interface IProviderOAuthAdapterRegistry
{
    IProviderOAuthAdapter Get(ProviderKind provider);
}

public sealed class ProviderOAuthAdapterRegistry : IProviderOAuthAdapterRegistry
{
    private readonly IReadOnlyDictionary<ProviderKind, IProviderOAuthAdapter> _adapters;

    public ProviderOAuthAdapterRegistry()
        : this(
            new CodexOAuthAdapter(),
            new GitHubOAuthAdapter(),
            new OpenRouterOAuthAdapter(),
            new AwsBuilderIdOAuthAdapter())
    {
    }

    public ProviderOAuthAdapterRegistry(params IProviderOAuthAdapter[] adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (adapters.Any(adapter => adapter is null))
        {
            throw new ArgumentException("Adapter collection cannot contain null entries.", nameof(adapters));
        }

        var duplicate = adapters
            .GroupBy(adapter => adapter.Provider)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Multiple OAuth adapters registered for provider {duplicate.Key}.",
                nameof(adapters));
        }

        _adapters = adapters.ToDictionary(adapter => adapter.Provider);
    }

    public IProviderOAuthAdapter Get(ProviderKind provider) =>
        _adapters.TryGetValue(provider, out var adapter)
            ? adapter
            : throw new NotSupportedException($"OAuth automation not supported for provider {provider}.");
}
