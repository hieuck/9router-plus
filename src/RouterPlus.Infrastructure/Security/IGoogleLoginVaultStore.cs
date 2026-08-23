using RouterPlus.Core.Security;

namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Storage interface for encrypted Google login vault.
/// </summary>
public interface IGoogleLoginVaultStore
{
    /// <summary>
    /// Creates a new vault at the specified path with the given password.
    /// </summary>
    Task<GoogleLoginVaultSession> CreateAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an existing vault using the provided password.
    /// </summary>
    Task<GoogleLoginVaultSession> OpenAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to open the vault using DPAPI-remembered unlock.
    /// Returns null if no remembered key exists or if it's invalid.
    /// </summary>
    Task<GoogleLoginVaultSession?> TryOpenRememberedAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current vault state to disk.
    /// </summary>
    Task SaveAsync(
        GoogleLoginVaultSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the vault to a portable encrypted file with a new password.
    /// </summary>
    Task ExportAsync(
        GoogleLoginVaultSession session,
        string destinationPath,
        string exportPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a vault from a source file, creating a backup of the current vault
    /// before replacing it. Invalidates any remembered unlock for the previous vault.
    /// </summary>
    Task ImportAsync(
        string currentPath,
        string sourcePath,
        string sourcePassword,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an open vault session with its decrypted content and crypto material.
/// </summary>
public interface GoogleLoginVaultSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique vault identifier.
    /// </summary>
    string VaultId { get; }

    /// <summary>
    /// Gets the current vault content.
    /// </summary>
    GoogleLoginVault Vault { get; }

    /// <summary>
    /// Replaces the vault content with a new version.
    /// </summary>
    void Replace(GoogleLoginVault vault);

    /// <summary>
    /// Stores a DPAPI-protected remembered key for this vault.
    /// </summary>
    Task RememberAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the DPAPI-protected remembered key file.
    /// </summary>
    Task RemoveRememberedAsync(CancellationToken cancellationToken = default);
}
