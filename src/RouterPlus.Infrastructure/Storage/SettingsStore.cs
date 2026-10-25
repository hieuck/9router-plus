using System.Collections.Concurrent;
using System.Text.Json;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Storage;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Storage;

/// <summary>
/// Provides asynchronous and synchronous access to user settings stored in JSON file.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>
    /// Locks used to prevent concurrent writes to the settings file.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Path to the settings JSON file.
    /// </summary>
    private readonly string _filePath;
    /// <summary>
    /// JSON serializer options with indentation enabled.
    /// </summary>
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsStore"/> class.
    /// If <paramref name="filePath"/> is null, defaults to %LocalAppData%\9RouterPlus\settings.json.
    /// </summary>
    /// <param name="filePath">Optional custom path to the settings file.</param>
    public SettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.SettingsPath;
    }

    /// <summary>
    /// Asynchronously loads the settings from file. Returns defaults if file missing or corrupt.
    /// Protected dashboard passwords are decrypted transparently; legacy plaintext is preserved.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The loaded <see cref="RouterSettings"/>.</returns>
    public async Task<RouterSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var raw = await LoadRawAsync(cancellationToken);
        return DecryptDashboardPassword(raw);
    }

    /// <summary>
    /// Synchronously loads the settings from file. Returns defaults if file missing or corrupt.
    /// </summary>
    /// <returns>The loaded <see cref="RouterSettings"/>.</returns>
    public RouterSettings Load()
    {
        var raw = LoadRaw();
        return DecryptDashboardPassword(raw);
    }

    /// <summary>
    /// Asynchronously saves the settings to file.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SaveAsync(RouterSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await WithSaveLockAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Asynchronously updates the quota auto-disable markers in the settings file.
    /// Uses a raw load so the dashboard password is never decrypted or re-encrypted here.
    /// </summary>
    /// <param name="markers">The markers to store.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task UpdateQuotaAutoDisableMarkersAsync(
        IReadOnlyList<QuotaAutoDisableMarker> markers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markers);

        var saveLock = SaveLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadRawAsync(cancellationToken);
            await WriteAsync(current with { QuotaAutoDisableMarkers = markers.ToArray() }, cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    /// <summary>
    /// Migrates a legacy plaintext dashboard password to its DPAPI-protected form.
    /// Idempotent; no-op when the password is null or already protected.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    public async Task MigrateDashboardPasswordAsync(CancellationToken cancellationToken = default)
    {
        var saveLock = SaveLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            var raw = await LoadRawAsync(cancellationToken);
            if (raw.DashboardAuthPassword is null)
            {
                return;
            }

            if (DashboardPasswordProtector.IsProtected(raw.DashboardAuthPassword))
            {
                return;
            }

            await WriteAsync(raw, cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task WithSaveLockAsync(RouterSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var saveLock = SaveLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(settings, cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task<RouterSettings> LoadRawAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new RouterSettings { UseLightTheme = true };
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<RouterSettings>(stream, _jsonOptions, cancellationToken)
                ?? new RouterSettings();
        }
        catch (JsonException)
        {
            // Return defaults if JSON is corrupted or incompatible
            return new RouterSettings();
        }
    }

    private RouterSettings LoadRaw()
    {
        if (!File.Exists(_filePath))
        {
            return new RouterSettings { UseLightTheme = true };
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<RouterSettings>(json, _jsonOptions) ?? new RouterSettings();
        }
        catch (JsonException)
        {
            // Return defaults if JSON is corrupted or incompatible
            return new RouterSettings();
        }
    }

    private static RouterSettings DecryptDashboardPassword(RouterSettings raw)
    {
        var password = raw.DashboardAuthPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            return raw;
        }

        if (!DashboardPasswordProtector.IsProtected(password))
        {
            // Legacy plaintext: preserve as-is.
            return raw;
        }

        if (DashboardPasswordProtector.TryUnprotect(password, out var plaintext) && !string.IsNullOrEmpty(plaintext))
        {
            return raw with { DashboardAuthPassword = plaintext };
        }

        // Corrupt/unreadable protected value: do not crash; drop it and warn once.
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Warning,
            "Settings",
            "DashboardPasswordUnprotectFailed",
            "Protected dashboard password could not be read; authentication password reset to null.",
            null);

        return raw with { DashboardAuthPassword = null };
    }

    private async Task WriteAsync(RouterSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Normalize the password to its protected form. Leave already-protected values untouched.
        string? protectedPassword = null;
        var password = settings.DashboardAuthPassword;
        if (!string.IsNullOrWhiteSpace(password))
        {
            if (DashboardPasswordProtector.IsProtected(password))
            {
                protectedPassword = password;
            }
            else
            {
                try
                {
                    protectedPassword = DashboardPasswordProtector.Protect(password!);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("Unable to protect the dashboard password with DPAPI.", exception);
                }
            }
        }

        var toWrite = settings with { DashboardAuthPassword = protectedPassword };

        var temporaryPath = _filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, toWrite, _jsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, true);
        }
        catch
        {
            // Clean up temp file on failure
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best effort cleanup
            }
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Preserve the original save result; cleanup can be retried on the next save.
            }
        }
    }
}