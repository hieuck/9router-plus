using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class ProviderDisplayStatusTests
{
    [Theory]
    [InlineData(ProviderHealthState.Healthy, "Online", "Healthy")]
    [InlineData(ProviderHealthState.Disabled, "Disable", "Disabled")]
    [InlineData(ProviderHealthState.Error, "Error", "Error")]
    [InlineData(ProviderHealthState.Missing, "Not added", "Missing")]
    [InlineData(ProviderHealthState.Unknown, "Checking", "Unknown")]
    public void From_maps_health_state_to_card_display(
        ProviderHealthState state,
        string expectedLabel,
        string expectedColorKey)
    {
        var display = ProviderDisplayStatus.From(state);

        Assert.Equal(expectedLabel, display.Label);
        Assert.Equal(expectedColorKey, display.ColorKey);
    }
}
