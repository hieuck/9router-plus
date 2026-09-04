using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Infrastructure service for profile health checks with caching.
/// </summary>
public sealed class ProfileHealthService
{
    private readonly ProfileHealthChecker _checker;
    private readonly GoogleAccountVault? _vault;
    private readonly Dictionary<CacheKey, CachedHealthStatus> _cache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>
    /// Create a new ProfileHealthService.
    /// </summary>
    /// <param name="vault">Optional Google account vault for credentials checks</param>
    public ProfileHealthService(GoogleAccountVault? vault = null)
    {
        _checker = new ProfileHealthChecker();
        _vault = vault;
    }

    /// <summary>
    /// Get health status for a profile. Returns cached result if available and not expired.
    /// </summary>
    /// <param name="profile">Profile to check</param>
    /// <param name="forceRefresh">If true, bypasses cache and performs fresh check</param>
    public async Task<ProfileHealthStatus> GetHealthStatusAsync(
        ChromeProfile profile,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var key = new CacheKey(profile.Id);

        await _cacheLock.WaitAsync();
        try
        {
            // Check cache if not forcing refresh
            if (!forceRefresh && _cache.TryGetValue(key, out var cached) && !cached.IsExpired)
            {
                return cached.Status;
            }

            // Perform health check
            var status = await Task.Run(() => PerformHealthCheck(profile));

            // Cache result
            _cache[key] = new CachedHealthStatus
            {
                Status = status,
                CachedAt = DateTime.UtcNow
            };

            return status;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Invalidate cached health status for a specific profile.
    /// </summary>
    public void InvalidateCache(ChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var key = new CacheKey(profile.Id);
        _cacheLock.Wait();
        try
        {
            _cache.Remove(key);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Invalidate all cached health statuses.
    /// </summary>
    public void InvalidateAllCache()
    {
        _cacheLock.Wait();
        try
        {
            _cache.Clear();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private ProfileHealthStatus PerformHealthCheck(ChromeProfile profile)
    {
        var allIssues = new List<HealthIssue>();

        // Filesystem checks
        var filesystemIssues = _checker.CheckFilesystemHealth(profile);
        allIssues.AddRange(filesystemIssues);

        // Credentials checks
        var credentialsIssues = _checker.CheckCredentialsHealth(profile, _vault);
        allIssues.AddRange(credentialsIssues);

        return ProfileHealthStatus.FromIssues(allIssues);
    }

    private record struct CacheKey(string ProfileId);

    private sealed class CachedHealthStatus
    {
        public required ProfileHealthStatus Status { get; init; }
        public required DateTime CachedAt { get; init; }
        public TimeSpan TTL { get; init; } = TimeSpan.FromMinutes(5);

        public bool IsExpired => DateTime.UtcNow - CachedAt > TTL;
    }
}
