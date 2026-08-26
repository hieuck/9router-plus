using System;
using System.IO;

namespace RouterPlus.App.E2E;

/// <summary>
/// Environment configuration for live E2E tests that interact with real Chrome profiles and credentials.
/// </summary>
public static class LiveTestEnvironment
{
    private const string EnabledVar = "ROUTERPLUS_LIVE_E2E";
    private const string ProfileVar = "ROUTERPLUS_LIVE_PROFILE";

    /// <summary>
    /// Whether live E2E tests are enabled.
    /// </summary>
    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(EnabledVar) == "1";

    /// <summary>
    /// Profile name to use for live tests (e.g., "Default").
    /// </summary>
    public static string? ProfileName =>
        Environment.GetEnvironmentVariable(ProfileVar);

    /// <summary>
    /// Validates that live test environment is properly configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">When environment is not configured.</exception>
    public static void RequireLiveEnvironment()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException(
                $"Live E2E tests require {EnabledVar}=1 environment variable. " +
                "These tests interact with real Chrome profiles and saved credentials.");
        }

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            throw new InvalidOperationException(
                $"Live E2E tests require {ProfileVar} environment variable (e.g., 'Default').");
        }
    }


    /// <summary>
    /// Gets the configured Chrome profile name, throwing if not configured.
    /// </summary>
    public static string GetRequiredProfileName()
    {
        RequireLiveEnvironment();
        return ProfileName!;
    }
}
