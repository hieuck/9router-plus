using System.Collections.Generic;
using System.IO;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.TestHelpers;

/// <summary>
/// Shared synthetic provider data for tests, so a shape change to
/// ProviderConnection only has to be made in one place.
/// </summary>
public static class TestData
{
    public static ProviderConnection CreateConnection(
        ProviderKind provider,
        IReadOnlyList<ProviderQuota>? quotas = null,
        long? usageCount = null,
        long? limitCount = null) =>
        new(
            "synthetic-connection",
            provider,
            "Synthetic",
            1,
            true,
            UsageCount: usageCount,
            LimitCount: limitCount,
            Quotas: quotas);

    public static ChromeProfile CreateProfile(
        string name = "Synthetic",
        string directoryName = "Default",
        string? userDataDirectory = null)
    {
        var root = userDataDirectory ?? CreateTempDirectory();
        return new ChromeProfile(
            ChromeProfile.CreateId(root, directoryName),
            name,
            directoryName,
            root,
            false);
    }

    public static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rp-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
