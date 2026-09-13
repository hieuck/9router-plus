using System;
using System.Collections.Generic;
using System.IO;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.Tests.TestHelpers
{
    public static class TestData
    {
        public static ProviderConnection CreateConnection(
            ProviderKind provider,
            string profileName = "Profile 1",
            IReadOnlyList<ProviderQuota>? quotas = null,
            long? usageCount = null,
            long? limitCount = null) =>
            new(
                "synthetic-connection",
                provider,
                profileName,
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
            var directory = Path.Combine(Path.GetTempPath(), "rp-app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}