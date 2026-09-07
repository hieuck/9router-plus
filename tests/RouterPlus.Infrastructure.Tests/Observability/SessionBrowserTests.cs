using System;
using System.IO;
using System.Linq;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class SessionBrowserTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        "RouterPlus.SessionBrowserTests",
        Guid.NewGuid().ToString("N"));

    private readonly ObservabilityPaths _paths;

    public SessionBrowserTests()
    {
        _paths = new ObservabilityPaths(_rootDirectory);
    }

    [Fact]
    public void Constructor_throws_for_null_paths()
    {
        // Arrange
        ObservabilityPaths? paths = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SessionBrowser(paths!));
    }

    [Fact]
    public void GetSessionInfo_returns_null_when_session_id_is_null()
    {
        // Arrange
        var browser = new SessionBrowser(_paths);

        // Act
        var info = browser.GetSessionInfo(null!);

        // Assert
        Assert.Null(info);
    }

    [Fact]
    public void DeleteSession_returns_false_when_session_id_is_null()
    {
        // Arrange
        var browser = new SessionBrowser(_paths);

        // Act
        var deleted = browser.DeleteSession(null!);

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public void ListSessions_returns_empty_when_no_sessions_exist()
    {
        // Arrange
        var browser = new SessionBrowser(_paths);

        // Act
        var sessions = browser.ListSessions();

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    public void ListSessions_returns_sessions_newest_first_including_empty_sessions()
    {
        // Arrange
        CreateSession("older", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "old data");
        CreateSession("newer", new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), "new data");
        Directory.CreateDirectory(_paths.GetSessionDirectory("empty"));
        var browser = new SessionBrowser(_paths);

        // Act
        var sessions = browser.ListSessions();

        // Assert
        Assert.Equal(new[] { "empty", "newer", "older" }, sessions.Select(session => session.SessionId));
        Assert.Equal(2, sessions.Count(session => session.FileCount == 1));
    }

    [Fact]
    public void GetSessionInfo_returns_file_metadata_and_artifact_flags()
    {
        // Arrange
        const string sessionId = "session-with-artifacts";
        var sessionDirectory = _paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(_paths.GetEventsFilePath(sessionId), "event");
        File.WriteAllText(_paths.GetSnapshotsFilePath(sessionId), "snapshot");
        File.WriteAllText(Path.Combine(sessionDirectory, "notes.txt"), "12345");
        var browser = new SessionBrowser(_paths);

        // Act
        var info = browser.GetSessionInfo(sessionId);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(sessionId, info.SessionId);
        Assert.Equal(3, info.FileCount);
        Assert.Equal(5 + 8 + 5, info.TotalSizeBytes);
        Assert.True(info.HasEvents);
        Assert.True(info.HasSnapshots);
        Assert.Equal(info.TotalSizeBytes / (1024.0 * 1024.0), info.SizeMB);
    }

    [Fact]
    public void GetSessionInfo_uses_utc_now_for_session_without_files()
    {
        // Arrange
        const string sessionId = "empty-session";
        Directory.CreateDirectory(_paths.GetSessionDirectory(sessionId));
        var before = DateTime.UtcNow;
        var browser = new SessionBrowser(_paths);

        // Act
        var info = browser.GetSessionInfo(sessionId);
        var after = DateTime.UtcNow;

        // Assert
        Assert.NotNull(info);
        Assert.InRange(info.StartTime, before, after);
        Assert.Equal(0, info.FileCount);
        Assert.Equal(0, info.TotalSizeBytes);
        Assert.False(info.HasEvents);
        Assert.False(info.HasSnapshots);
    }

    [Fact]
    public void DeleteSession_deletes_existing_session_recursively()
    {
        // Arrange
        const string sessionId = "deletable";
        var sessionDirectory = _paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "nested"));
        File.WriteAllText(Path.Combine(sessionDirectory, "session.json"), "metadata");
        File.WriteAllText(Path.Combine(sessionDirectory, "nested", "details.txt"), "details");
        var browser = new SessionBrowser(_paths);

        // Act
        var deleted = browser.DeleteSession(sessionId);

        // Assert
        Assert.True(deleted);
        Assert.False(Directory.Exists(sessionDirectory));
        Assert.False(browser.DeleteSession(sessionId));
    }

    [Fact]
    public void DeleteOldSessions_deletes_only_sessions_before_cutoff()
    {
        // Arrange
        CreateSession("old", DateTime.UtcNow.AddDays(-10), "old");
        CreateSession("recent", DateTime.UtcNow.AddDays(-1), "recent");
        var browser = new SessionBrowser(_paths);

        // Act
        var deletedCount = browser.DeleteOldSessions(5);

        // Assert
        Assert.Equal(1, deletedCount);
        Assert.False(Directory.Exists(_paths.GetSessionDirectory("old")));
        Assert.True(Directory.Exists(_paths.GetSessionDirectory("recent")));
    }

    private void CreateSession(string sessionId, DateTime creationTimeUtc, string contents)
    {
        var sessionDirectory = _paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        var filePath = Path.Combine(sessionDirectory, "session.json");
        File.WriteAllText(filePath, contents);
        File.SetCreationTimeUtc(filePath, creationTimeUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
