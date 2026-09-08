using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

public sealed class GoogleAccountVaultPathsTests
{
    [Fact]
    public void Constructor_uses_custom_root_for_vault_and_remembered_key_paths()
    {
        var root = Path.Combine("synthetic", "vault-root");
        var paths = new GoogleAccountVaultPaths(root);

        Assert.Equal(Path.Combine(root, "google-accounts.vault"), paths.VaultPath);
        Assert.Equal(Path.Combine(root, "google-accounts.remembered"), paths.RememberedKeyPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_rejects_missing_root(string? root)
    {
        Assert.ThrowsAny<ArgumentException>(() => new GoogleAccountVaultPaths(root!));
    }

    [Fact]
    public void Constructor_preserves_custom_root_without_creating_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var paths = new GoogleAccountVaultPaths(root);

        Assert.False(Directory.Exists(root));
        Assert.StartsWith(root, paths.VaultPath, StringComparison.Ordinal);
        Assert.StartsWith(root, paths.RememberedKeyPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_constructor_uses_local_application_data_storage_root()
    {
        var paths = new GoogleAccountVaultPaths();
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus");

        Assert.Equal(Path.Combine(expectedRoot, "google-accounts.vault"), paths.VaultPath);
        Assert.Equal(Path.Combine(expectedRoot, "google-accounts.remembered"), paths.RememberedKeyPath);
    }
}
