namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Provides default local storage paths for the Google login vault.
/// </summary>
public sealed class GoogleLoginVaultPaths
{
    private readonly string _rootDirectory;

    /// <summary>
    /// Creates paths using the default %LOCALAPPDATA%\9RouterPlus directory.
    /// </summary>
    public GoogleLoginVaultPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus"))
    {
    }

    /// <summary>
    /// Creates paths using a custom root directory (for testing).
    /// </summary>
    /// <param name="rootDirectory">Custom root directory path.</param>
    public GoogleLoginVaultPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory, nameof(rootDirectory));
        _rootDirectory = rootDirectory;
    }

    /// <summary>
    /// Gets the path to the encrypted vault file.
    /// </summary>
    public string VaultPath => Path.Combine(_rootDirectory, "google-login-vault.gvault");

    /// <summary>
    /// Gets the path to the DPAPI-protected remembered key file.
    /// </summary>
    public string RememberedKeyPath => Path.Combine(_rootDirectory, "google-login-vault.remembered");
}
