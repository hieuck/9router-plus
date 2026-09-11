using System.Collections.Generic;
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
}
