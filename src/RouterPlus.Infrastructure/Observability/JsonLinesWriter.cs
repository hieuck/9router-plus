using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.Core.Observability;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Writes observability events to JSON Lines format.
/// </summary>
public sealed class JsonLinesWriter : IObservabilityWriter
{
    private readonly ObservabilityPaths _paths;
    private readonly string _sessionId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50MB

    public JsonLinesWriter(ObservabilityPaths paths, string sessionId)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));

        _jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task WriteEventsAsync(IEnumerable<LogEvent> events)
    {
        if (_disposed) return;

        await _writeLock.WaitAsync();
        try
        {
            var eventsFilePath = _paths.GetEventsFilePath(_sessionId);

            // Check if file rotation needed
            if (File.Exists(eventsFilePath))
            {
                var fileInfo = new FileInfo(eventsFilePath);
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    await RotateFileAsync(eventsFilePath);
                }
            }

            // Append events as JSON Lines
            await using var writer = new StreamWriter(eventsFilePath, append: true);
            foreach (var evt in events)
            {
                var json = JsonSerializer.Serialize(evt, _jsonOptions);
                await writer.WriteLineAsync(json);
            }
        }
        catch
        {
            // Never crash app due to write failure
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
    {
        if (_disposed) return;

        await _writeLock.WaitAsync();
        try
        {
            var snapshotsFilePath = _paths.GetSnapshotsFilePath(_sessionId);

            // Check if file rotation needed
            if (File.Exists(snapshotsFilePath))
            {
                var fileInfo = new FileInfo(snapshotsFilePath);
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    await RotateFileAsync(snapshotsFilePath);
                }
            }

            // Append snapshots as JSON Lines
            await using var writer = new StreamWriter(snapshotsFilePath, append: true);
            foreach (var snapshot in snapshots)
            {
                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                await writer.WriteLineAsync(json);
            }
        }
        catch
        {
            // Never crash app due to write failure
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private Task RotateFileAsync(string filePath)
    {
        try
        {
            // Rename to events.1.jsonl, events.2.jsonl, etc.
            var directory = Path.GetDirectoryName(filePath);
            var baseName = Path.GetFileNameWithoutExtension(filePath); // "events"
            var extension = Path.GetExtension(filePath); // ".jsonl"

            var rotationNumber = 1;
            string rotatedPath;
            do
            {
                rotatedPath = Path.Combine(directory!, $"{baseName}.{rotationNumber}{extension}");
                rotationNumber++;
            } while (File.Exists(rotatedPath));

            File.Move(filePath, rotatedPath);
        }
        catch
        {
            // If rotation fails, continue writing to existing file
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();
    }
}
