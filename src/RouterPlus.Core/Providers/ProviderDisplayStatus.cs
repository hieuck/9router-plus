namespace RouterPlus.Core.Providers;

public sealed record ProviderDisplayStatus(string Label, string ColorKey)
{
    public static ProviderDisplayStatus From(ProviderHealthState state) => state switch
    {
        ProviderHealthState.Healthy => new("Online", "Healthy"),
        ProviderHealthState.Disabled => new("Disable", "Disabled"),
        ProviderHealthState.Error => new("Error", "Error"),
        ProviderHealthState.Missing => new("Not added", "Missing"),
        _ => new("Checking", "Unknown")
    };
}
