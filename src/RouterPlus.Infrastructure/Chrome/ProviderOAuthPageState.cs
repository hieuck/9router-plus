namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Provider-specific page state (non-Google pages).
/// Subclasses extend with provider-specific fields.
/// </summary>
public abstract record ProviderOAuthPageState
{
    public required string CurrentUrl { get; init; }
}