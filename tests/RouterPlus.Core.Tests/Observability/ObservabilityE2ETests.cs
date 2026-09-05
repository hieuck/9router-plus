using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class ObservabilityE2ETests
{
    [Fact]
    public async Task E2E_ProfileHealthCheck_logs_complete_workflow()
    {
        // Arrange: Create real test profile structure
        var testSessionDir = Path.Combine(Path.GetTempPath(), "RouterPlusE2ETests", Guid.NewGuid().ToString());
        var userDataDir = Path.Combine(testSessionDir, "ChromeUserData");
        var profileDir = Path.Combine(userDataDir, "Default");
        Directory.CreateDirectory(profileDir);

        // Create realistic Chrome profile files
        File.WriteAllText(Path.Combine(profileDir, "Preferences"), "{}");
        File.WriteAllText(Path.Combine(profileDir, "Cookies"), "fake_cookie_data");
        File.WriteAllText(Path.Combine(profileDir, "History"), "fake_history_data");

        try
        {
            // Setup observability
            var paths = new ObservabilityPaths();
            var sessionManager = new SessionManager(paths);
            sessionManager.Initialize();

            var writer = new JsonLinesWriter(paths, sessionManager.SessionId);
            ObservabilityHub.Instance.SetWriter(writer);

            var profileId = ChromeProfile.CreateId(userDataDir, "Default");
            var profile = new ChromeProfile(
                Id: profileId,
                Name: "E2E Test Profile",
                DirectoryName: "Default",
                UserDataDirectory: userDataDir,
                IsDefault: true);

            // Act: Run health check (simulates real user action)
            var checker = new RouterPlus.Core.Chrome.ProfileHealthChecker();
            var filesystemIssues = checker.CheckFilesystemHealth(profile);

            // Flush events
            await ObservabilityHub.Instance.FlushAsync();
            Thread.Sleep(500);

            // Assert: Verify observability captured the workflow
            var eventsFile = paths.GetEventsFilePath(sessionManager.SessionId);
            Assert.True(File.Exists(eventsFile), "events.jsonl should exist");

            var events = File.ReadAllLines(eventsFile)
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .ToList();

            // Filter events for this specific profile
            var profileEvents = events.Where(e =>
            {
                if (e.TryGetProperty("context", out var ctx) &&
                    ctx.TryGetProperty("profile", out var prof))
                {
                    return prof.GetString() == "E2E Test Profile";
                }
                return false;
            }).ToList();

            Assert.True(profileEvents.Count >= 2, $"Should have at least 2 events for E2E Test Profile, found {profileEvents.Count}");

            // Should have FilesystemCheckStarted
            var startEvent = profileEvents.FirstOrDefault(e =>
                e.GetProperty("event").GetString() == "FilesystemCheckStarted");
            Assert.False(startEvent.Equals(default(JsonElement)), "Should log FilesystemCheckStarted");

            // Verify context has profile info
            var context = startEvent.GetProperty("context");
            Assert.True(context.TryGetProperty("profile", out var profileName));
            Assert.Equal("E2E Test Profile", profileName.GetString());

            // Should have FilesystemCheckCompleted
            var completeEvent = profileEvents.FirstOrDefault(e =>
                e.GetProperty("event").GetString() == "FilesystemCheckCompleted");
            Assert.False(completeEvent.Equals(default(JsonElement)), "Should log FilesystemCheckCompleted");

            // Verify issue count matches
            var completeContext = completeEvent.GetProperty("context");
            Assert.True(completeContext.TryGetProperty("issue_count", out var issueCount));
            Assert.Equal(filesystemIssues.Count, issueCount.GetInt32());

            // Cleanup
            writer.Dispose();
            try { Directory.Delete(paths.GetSessionDirectory(sessionManager.SessionId), true); } catch { }
        }
        finally
        {
            try { Directory.Delete(testSessionDir, true); } catch { }
        }
    }
}
