using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed record ProfileProviderFilterOption(ProviderKind? Kind, string DisplayName)
{
    public override string ToString() => DisplayName;
}
