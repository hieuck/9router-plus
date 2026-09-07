using System.Reflection;
using System.Text;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public class SessionBrowserTests
{
    [Fact]
    public void ListSessions_returns_empty_when_no_sessions_exist()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);

        // Act
        var sessions = browser.ListSessions();

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    public void GetSessionInfo_returns_null_for_nonexistent_session()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);

        // Act
        var info = browser.GetSessionInfo("nonexistent_session_id");

        // Assert
        Assert.Null(info);
    }

    [Fact]
    public void DeleteSession_returns_false_for_nonexistent_session()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);

        // Act
        var deleted = browser.DeleteSession("nonexistent_session_id");

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public void GetSessionInfo_returns_file_metadata_for_session()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);
        var createdAt = DateTime.UtcNow.AddHours(-2);
        var files = new[]
        {
            ("session.json", "metadata"),
            ("events.jsonl", "event"),
            ("snapshots.jsonl", "snapshot")
        };
        CreateSession(fixture, "session-with-metadata", createdAt, files);
        var expectedSize = files.Sum(file => Encoding.UTF8.GetByteCount(file.Item2));

        // Act
        var info = browser.GetSessionInfo("session-with-metadata");

        // Assert
        Assert.NotNull(info);
        Assert.Equal("session-with-metadata", info.SessionId);
        Assert.Equal(files.Length, info.FileCount);
        Assert.Equal(expectedSize, info.TotalSizeBytes);
        Assert.True(info.HasEvents);
        Assert.True(info.HasSnapshots);
        Assert.InRange(info.StartTime, createdAt.AddSeconds(-1), createdAt.AddSeconds(1));
        Assert.Equal(expectedSize / (1024.0 * 1024.0), info.SizeMB);
    }

    [Fact]
    public void ListSessions_returns_sessions_newest_first()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);
        CreateSession(fixture, "old-session", DateTime.UtcNow.AddDays(-3), ("events.jsonl", "old"));
        CreateSession(fixture, "newest-session", DateTime.UtcNow.AddHours(-1), ("events.jsonl", "newest"));
        CreateSession(fixture, "middle-session", DateTime.UtcNow.AddDays(-1), ("events.jsonl", "middle"));

        // Act
        var sessions = browser.ListSessions();

        // Assert
        Assert.Equal(
            new[] { "newest-session", "middle-session", "old-session" },
            sessions.Select(session => session.SessionId));
    }

    [Fact]
    public void DeleteSession_deletes_session_files_and_directory()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);
        var sessionDirectory = CreateSession(
            fixture,
            "session-to-delete",
            DateTime.UtcNow.AddHours(-1),
            ("events.jsonl", "event"));
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "nested"));
        File.WriteAllText(Path.Combine(sessionDirectory, "nested", "extra.json"), "extra");

        // Act
        var deleted = browser.DeleteSession("session-to-delete");

        // Assert
        Assert.True(deleted);
        Assert.False(Directory.Exists(sessionDirectory));
    }

    [Fact]
    public void DeleteOldSessions_deletes_only_sessions_older_than_retention_cutoff()
    {
        // Arrange
        using var fixture = new TempObservabilityFixture();
        var browser = new SessionBrowser(fixture.Paths);
        CreateSession(fixture, "expired-session", DateTime.UtcNow.AddDays(-30), ("events.jsonl", "old"));
        CreateSession(fixture, "active-session", DateTime.UtcNow.AddDays(-1), ("events.jsonl", "recent"));

        // Act
        var deletedCount = browser.DeleteOldSessions(olderThanDays: 7);

        // Assert
        Assert.Equal(1, deletedCount);
        Assert.False(Directory.Exists(fixture.Paths.GetSessionDirectory("expired-session")));
        Assert.True(Directory.Exists(fixture.Paths.GetSessionDirectory("active-session")));
    }

    private static string CreateSession(
        TempObservabilityFixture fixture,
        string sessionId,
        DateTime createdAt,
        params (string FileName, string Content)[] files)
    {
        var sessionDirectory = fixture.Paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);

        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(sessionDirectory, fileName);
            File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes(content));
            File.SetCreationTimeUtc(filePath, createdAt);
        }

        return sessionDirectory;
    }

    private sealed class TempObservabilityFixture : IDisposable
    {
        private static readonly FieldInfo RootDirectoryField =
            typeof(ObservabilityPaths).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo SessionsDirectoryField =
            typeof(ObservabilityPaths).GetField("<SessionsDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public TempObservabilityFixture()
        {
            RootDirectory = Directory.CreateTempSubdirectory("routerplus-session-browser-").FullName;
            Paths = new ObservabilityPaths();
            RootDirectoryField.SetValue(Paths, RootDirectory);
            SessionsDirectoryField.SetValue(Paths, Path.Combine(RootDirectory, "sessions"));
            Directory.CreateDirectory(Paths.SessionsDirectory);
        }

        public string RootDirectory { get; }

        public ObservabilityPaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
