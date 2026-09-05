using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Captures and persists application state snapshots for debugging.
/// </summary>
public sealed class SnapshotManager : IDisposable
{
    private readonly ObservabilityPaths _paths;
    private readonly string _sessionId;
    private readonly Timer _periodicTimer;
    private readonly JsonSerializerOptions _jsonOptions;
    private object? _lastSnapshot;
    private bool _disposed;

    private const int SnapshotIntervalSeconds = 60;
    private const int MaxSnapshotSizeBytes = 1024 * 1024; // 1MB uncompressed

    public SnapshotManager(ObservabilityPaths paths, string sessionId)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Start periodic snapshot timer
        _periodicTimer = new Timer(
            PeriodicSnapshotCallback,
            null,
            TimeSpan.FromSeconds(SnapshotIntervalSeconds),
            TimeSpan.FromSeconds(SnapshotIntervalSeconds));
    }

    /// <summary>
    /// Capture a state snapshot on demand.
    /// </summary>
    public Task CaptureSnapshotAsync(object state, string reason)
    {
        if (_disposed) return Task.CompletedTask;

        return CaptureSnapshotInternalAsync(state, reason);
    }

    private void PeriodicSnapshotCallback(object? state)
    {
        // Timer callback runs on thread pool - no async/await here
        // Just check if snapshot changed and capture if needed
    }

    private async Task CaptureSnapshotInternalAsync(object state, string reason)
    {
        try
        {
            // Serialize state
            var json = JsonSerializer.Serialize(state, _jsonOptions);

            // Check if state changed from last snapshot
            var stateHash = ComputeHash(json);
            if (_lastSnapshot != null && stateHash.Equals(ComputeHash(JsonSerializer.Serialize(_lastSnapshot, _jsonOptions))))
            {
                return; // No change, skip snapshot
            }

            _lastSnapshot = state;

            // Check size
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
            if (jsonBytes.Length > MaxSnapshotSizeBytes)
            {
                // Compress large snapshots
                using var compressed = new MemoryStream();
                using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest))
                {
                    await gzip.WriteAsync(jsonBytes);
                }

                var snapshotPath = _paths.GetSnapshotFilePath(_sessionId, DateTime.UtcNow, compressed: true);
                await File.WriteAllBytesAsync(snapshotPath, compressed.ToArray());
            }
            else
            {
                var snapshotPath = _paths.GetSnapshotFilePath(_sessionId, DateTime.UtcNow, compressed: false);
                await File.WriteAllTextAsync(snapshotPath, json);
            }
        }
        catch
        {
            // Never crash app due to snapshot failure
        }
    }

    private static string ComputeHash(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _periodicTimer?.Dispose();
    }
}
