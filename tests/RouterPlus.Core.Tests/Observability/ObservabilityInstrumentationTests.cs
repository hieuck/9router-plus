using System.IO;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class ObservabilityInstrumentationTests
{
    [Fact]
    public async Task HealthCheck_logs_filesystem_check_events()
    {
        // Arrange
        var testSessionDir = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testSessionDir);

        try
        {
            var paths = new ObservabilityPaths();
            var sessionManager = new SessionManager(paths);
            sessionManager.Initialize();

            var writer = new JsonLinesWriter(paths, sessionManager.SessionId);
            ObservabilityHub.Instance.SetWriter(writer);

            var userDataDir = testSessionDir;
            var directoryName = "Default";
            var profileId = ChromeProfile.CreateId(userDataDir, directoryName);

            var profile = new ChromeProfile(
                Id: profileId,
                Name: "Test Profile",
                DirectoryName: directoryName,
                UserDataDirectory: userDataDir,
                IsDefault: true);

            Directory.CreateDirectory(profile.ProfilePath);

            var checker = new ProfileHealthChecker();

            // Act
            var issues = checker.CheckFilesystemHealth(profile);

            // Flush and wait for write
            await ObservabilityHub.Instance.FlushAsync();
            System.Threading.Thread.Sleep(500);

            // Assert - check events were logged
            var eventsFile = paths.GetEventsFilePath(sessionManager.SessionId);
            Assert.True(File.Exists(eventsFile), $"events.jsonl should exist at {eventsFile}");

            var events = File.ReadAllLines(eventsFile);
            Assert.Contains(events, e => e.Contains("FilesystemCheckStarted"));
            Assert.Contains(events, e => e.Contains("FilesystemCheckCompleted"));
        }
        finally
        {
            if (Directory.Exists(testSessionDir))
            {
                Directory.Delete(testSessionDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HealthCheck_logs_credentials_not_found()
    {
        // Arrange
        var testSessionDir = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testSessionDir);

        try
        {
            var paths = new ObservabilityPaths();
            var sessionManager = new SessionManager(paths);
            sessionManager.Initialize();

            var writer = new JsonLinesWriter(paths, sessionManager.SessionId);
            ObservabilityHub.Instance.SetWriter(writer);

            var userDataDir = testSessionDir;
            var directoryName = "Default";
            var profileId = ChromeProfile.CreateId(userDataDir, directoryName);

            var profile = new ChromeProfile(
                Id: profileId,
                Name: "Test Profile",
                DirectoryName: directoryName,
                UserDataDirectory: userDataDir,
                IsDefault: true);

            var vault = new GoogleAccountVault();
            var checker = new ProfileHealthChecker();

            // Act
            var issues = checker.CheckCredentialsHealth(profile, vault);

            // Flush and wait for write
            await ObservabilityHub.Instance.FlushAsync();
            System.Threading.Thread.Sleep(500);

            // Assert
            var eventsFile = paths.GetEventsFilePath(sessionManager.SessionId);
            var events = File.ReadAllLines(eventsFile);

            Assert.Contains(events, e => e.Contains("CredentialsCheckStarted"));
            Assert.Contains(events, e => e.Contains("CredentialsNotFound"));
            Assert.Contains(events, e => e.Contains("CredentialsCheckCompleted"));
        }
        finally
        {
            if (Directory.Exists(testSessionDir))
            {
                Directory.Delete(testSessionDir, recursive: true);
            }
        }
    }
}
