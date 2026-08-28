namespace RouterPlus.App.ViewModels;

/// <summary>
/// Represents the three-state filter for a provider.
/// </summary>
public enum ProviderFilterState
{
    /// <summary>
    /// Filter is off - show all profiles.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Show only profiles that HAVE this provider connected.
    /// </summary>
    Has = 1,

    /// <summary>
    /// Show only profiles that DO NOT HAVE this provider connected.
    /// </summary>
    NotHas = 2
}
