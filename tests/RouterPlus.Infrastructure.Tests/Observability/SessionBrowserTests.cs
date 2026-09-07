using System;
using System.IO;
using System.Reflection;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class SessionBrowserTests
{
    [Fact]
    public void GetSessionInfo_returns_populated_session_information()
    {
        using var fixture = new SessionBrowserFixture();
        var sessionId = "populated-session";
        var sessionDirectory = fixture.Paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);

        var eventsContent = new byte[] { 1, 2, 3 };
        var snapshotsContent = new byte[] { 4, 5 };
        File.WriteAllBytes(fixture.Paths.GetEventsFilePath(sessionId), eventsContent);
        File.WriteAllBytes(fixture.Paths.GetSnapshotsFilePath(sessionId), snapshotsContent);
        File.WriteAllBytes(Path.Combine(sessionDirectory, "session.json"), new byte[] { 6, 7, 8, 9 });

        var info = fixture.Browser.GetSessionInfo(sessionId);

        Assert.NotNull(info);
        Assert.Equal(sessionId, info.SessionId);
        Assert.Equal(9, info.TotalSizeBytes);
        Assert.Equal(3, info.FileCount);
        Assert.True(info.HasEvents);
        Assert.True(info.HasSnapshots);
        Assert.NotEqual(default, info.StartTime);
        Assert.Equal(9 / (1024.0 * 1024.0), info.SizeMB);
    }

    [Fact]
    public void ListSessions_returns_sessions_newest_first()
    {
        using var fixture = new SessionBrowserFixture();
        CreateSession(fixture.Paths, "older-session", DateTime.UtcNow.AddDays(-2));
        CreateSession(fixture.Paths, "newest-session", DateTime.UtcNow.AddHours(-1));
        CreateSession(fixture.Paths, "middle-session", DateTime.UtcNow.AddDays(-1));

        var sessions = fixture.Browser.ListSessions();

        Assert.Equal(
            new[] { "newest-session", "middle-session", "older-session" },
            sessions.ConvertAll(session => session.SessionId));
    }

    [Fact]
    public void ListSessions_skips_entries_that_are_not_valid_session_directories()
    {
        using var fixture = new SessionBrowserFixture();
        CreateSession(fixture.Paths, "valid-session", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(fixture.Paths.SessionsDirectory, "not-a-session-directory"), "ignored");

        var sessions = fixture.Browser.ListSessions();

        var session = Assert.Single(sessions);
        Assert.Equal("valid-session", session.SessionId);
    }

    [Fact]
    public void DeleteSession_deletes_existing_session_recursively()
    {
        using var fixture = new SessionBrowserFixture();
        var sessionId = "session-to-delete";
        var sessionDirectory = fixture.Paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "nested"));
        File.WriteAllText(Path.Combine(sessionDirectory, "events.jsonl"), "event");
        File.WriteAllText(Path.Combine(sessionDirectory, "nested", "snapshot.json"), "snapshot");

        var deleted = fixture.Browser.DeleteSession(sessionId);

        Assert.True(deleted);
        Assert.False(Directory.Exists(sessionDirectory));
    }

    [Fact]
    public void DeleteSession_returns_false_for_missing_session()
    {
        using var fixture = new SessionBrowserFixture();

        var deleted = fixture.Browser.DeleteSession("missing-session");

        Assert.False(deleted);
    }

    [Fact]
    public void DeleteOldSessions_deletes_only_sessions_older_than_cutoff()
    {
        using var fixture = new SessionBrowserFixture();
        var oldSessionId = "old-session";
        var recentSessionId = "recent-session";
        CreateSession(fixture.Paths, oldSessionId, DateTime.UtcNow.AddDays(-10));
        CreateSession(fixture.Paths, recentSessionId, DateTime.UtcNow.AddDays(-1));

        var deletedCount = fixture.Browser.DeleteOldSessions(7);

        Assert.Equal(1, deletedCount);
        Assert.False(Directory.Exists(fixture.Paths.GetSessionDirectory(oldSessionId)));
        Assert.True(Directory.Exists(fixture.Paths.GetSessionDirectory(recentSessionId)));
    }

    private static void CreateSession(ObservabilityPaths paths, string sessionId, DateTime creationTimeUtc)
    {
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        var filePath = Path.Combine(sessionDirectory, "session.json");
        File.WriteAllText(filePath, sessionId);
        File.SetCreationTimeUtc(filePath, creationTimeUtc);
    }

    private sealed class SessionBrowserFixture : IDisposable
    {
        private readonly string _rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "RouterPlusSessionBrowserTests",
            Guid.NewGuid().ToString("N"));

        public SessionBrowserFixture()
        {
            var sessionsDirectory = Path.Combine(_rootDirectory, "sessions");
            Directory.CreateDirectory(sessionsDirectory);

            Paths = new ObservabilityPaths();
            SetPath(Paths, nameof(ObservabilityPaths.RootDirectory), _rootDirectory);
            SetPath(Paths, nameof(ObservabilityPaths.SessionsDirectory), sessionsDirectory);
            Browser = new SessionBrowser(Paths);
        }

        public ObservabilityPaths Paths { get; }

        public SessionBrowser Browser { get; }

        public void Dispose()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }

        private static void SetPath(ObservabilityPaths paths, string propertyName, string value)
        {
            var field = typeof(ObservabilityPaths).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            field!.SetValue(paths, value);
        }
    }
}
