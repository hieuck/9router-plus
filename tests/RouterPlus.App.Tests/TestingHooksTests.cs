using System.Text.Json;
using RouterPlus.App.Testing;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Tests;

[Collection("TestingHooks")]
public sealed class TestingHooksTests : IDisposable
{
    private readonly string _stateFilePath = TestingHooks.GetStateFilePath(Environment.ProcessId);
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"RouterPlusTestingHooks_{Guid.NewGuid():N}");
    private readonly string? _originalEnabledValue;

    public TestingHooksTests()
    {
        _originalEnabledValue = Environment.GetEnvironmentVariable("ENABLE_TESTING_HOOKS");
        DeleteStateFile();
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENABLE_TESTING_HOOKS", _originalEnabledValue);
        TestingHooks.Initialize(CreateViewModel());
        DeleteStateFile();

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Initialize_WhenHooksAreDisabled_DoesNotRegisterStateOrWriteFile()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ENABLE_TESTING_HOOKS", "0");
        var viewModel = CreateViewModel();

        // Act
        TestingHooks.Initialize(viewModel);

        // Assert
        Assert.False(TestingHooks.Enabled);
        Assert.Null(TestingHooks.ReadStateFile(Environment.ProcessId));
    }

    [Fact]
    public void Initialize_WhenHooksAreEnabled_WritesAndTracksViewModelState()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ENABLE_TESTING_HOOKS", "true");
        var viewModel = CreateViewModel();
        var profile = CreateProfile("Hooked profile", "Default");
        viewModel.Profiles.Add(profile);
        var row = new ProfileRowViewModel(profile, viewModel.Providers);
        viewModel.ProfileRows.Add(row);

        // Act
        TestingHooks.Initialize(viewModel);
        row.HealthStatus = ProfileHealthStatus.FromIssues(
        [
            HealthIssue.Warning(
                HealthCategory.Credentials,
                "Credentials need attention",
                "Update the credentials")
        ]);
        row.IsCheckingHealth = true;
        row.IsCheckingHealth = false;

        // Assert
        Assert.True(TestingHooks.Enabled);
        var stateJson = TestingHooks.ReadStateFile(Environment.ProcessId);
        Assert.NotNull(stateJson);
        using var state = JsonDocument.Parse(stateJson);
        Assert.Equal(viewModel.StatusText, state.RootElement.GetProperty("StatusText").GetString());
        var profileState = Assert.Single(state.RootElement.GetProperty("Profiles").EnumerateArray());
        Assert.Equal("Hooked profile", profileState.GetProperty("Name").GetString());
        Assert.Equal(nameof(HealthLevel.Warning), profileState.GetProperty("HealthLevel").GetString());
        Assert.Equal("1 warning(s) detected", profileState.GetProperty("HealthMessage").GetString());
        Assert.Equal(1, profileState.GetProperty("IssueCount").GetInt32());
        Assert.False(profileState.GetProperty("IsCheckingHealth").GetBoolean());
    }

    [Fact]
    public void ReadStateFile_WhenStateFileDoesNotExist_ReturnsNull()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ENABLE_TESTING_HOOKS", "false");
        TestingHooks.Initialize(CreateViewModel());

        // Act
        var state = TestingHooks.ReadStateFile(Environment.ProcessId);

        // Assert
        Assert.Null(state);
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), $"routerplus-test-state-{Environment.ProcessId}.json"),
            TestingHooks.GetStateFilePath(Environment.ProcessId));
    }

    private MainViewModel CreateViewModel() => new(
        settingsStore: new SettingsStore(Path.Combine(_testDirectory, "settings.json")),
        googleLoginVaultPaths: new GoogleAccountVaultPaths(
            Path.Combine(_testDirectory, "Vault")),
        harnessProfiles: Array.Empty<ChromeProfile>());

    private static ChromeProfile CreateProfile(string name, string directoryName) => new(
        ChromeProfile.CreateId(Path.Combine(Path.GetTempPath(), "RouterPlusTestingHooksChrome"), directoryName),
        name,
        directoryName,
        Path.Combine(Path.GetTempPath(), "RouterPlusTestingHooksChrome"),
        false);

    private void DeleteStateFile()
    {
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
        }
    }
}

[CollectionDefinition("TestingHooks", DisableParallelization = true)]
public sealed class TestingHooksCollection;
