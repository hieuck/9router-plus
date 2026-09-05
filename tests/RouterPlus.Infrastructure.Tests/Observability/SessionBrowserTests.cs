using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public class SessionBrowserTests
{
    [Fact]
    public void ListSessions_returns_empty_when_no_sessions_exist()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);

        // Act
        var sessions = browser.ListSessions();

        // Assert
        Assert.NotNull(sessions);
    }

    [Fact]
    public void GetSessionInfo_returns_null_for_nonexistent_session()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);

        // Act
        var info = browser.GetSessionInfo("nonexistent_session_id");

        // Assert
        Assert.Null(info);
    }

    [Fact]
    public void DeleteSession_returns_false_for_nonexistent_session()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);

        // Act
        var deleted = browser.DeleteSession("nonexistent_session_id");

        // Assert
        Assert.False(deleted);
    }
}
