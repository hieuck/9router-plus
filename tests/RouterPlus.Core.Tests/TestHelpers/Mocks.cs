using System.IO;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.TestHelpers;

public static class Mocks
{
    public static ProviderConnectionVaultStore CreateProviderVault(string directory) =>
        new(Path.Combine(directory, "provider-connections.vault"));

    public static GoogleAccountVaultStore CreateGoogleVault(string directory) =>
        new(new GoogleAccountVaultPaths(directory));

    public static SettingsStore CreateSettingsStore(string directory) =>
        new(Path.Combine(directory, "settings.json"));
}