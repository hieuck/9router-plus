using Moq;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Services;
using Xunit;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// Tests for AutoLoginOrchestrator - Priority 1 Critical Tests
/// Verifies primary/fallback auth method logic and factory selection
///
/// NOTE: These tests verify orchestrator logic without actual Chrome automation.
/// Full integration tests with real Chrome require ROUTERPLUS_LIVE_E2E=1.
/// </summary>
public sealed class AutoLoginOrchestratorTests
{
    [Fact]
    public async Task LoginAsync_NoCredentialsConfigured_ReturnsNoCredentialsError()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vaultPaths = new GoogleAccountVaultPaths(tempDir);
            var googleVault = new GoogleAccountVaultStore(vaultPaths);
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            var mockChromeLauncher = new Mock<IChromeLauncher>();

            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                mockChromeLauncher.Object);

            // Act - No credentials configured, should return error immediately
            var result = await orchestrator.LoginAsync(
                "TestProfile",
                ProviderKind.Codex,
                new Uri("https://chatgpt.com"),
                TimeSpan.FromMinutes(1),
                CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("No credentials configured", result.ErrorMessage);
            Assert.Null(result.Method);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task LoginAsync_NullProfileName_ThrowsArgumentNullException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vaultPaths = new GoogleAccountVaultPaths(tempDir);
            var googleVault = new GoogleAccountVaultStore(vaultPaths);
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            var mockChromeLauncher = new Mock<IChromeLauncher>();

            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                mockChromeLauncher.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await orchestrator.LoginAsync(
                    null!,
                    ProviderKind.Codex,
                    new Uri("https://chatgpt.com"),
                    TimeSpan.FromMinutes(1),
                    CancellationToken.None);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoginAsync_NullStartUri_ThrowsArgumentNullException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vaultPaths = new GoogleAccountVaultPaths(tempDir);
            var googleVault = new GoogleAccountVaultStore(vaultPaths);
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            var mockChromeLauncher = new Mock<IChromeLauncher>();

            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                mockChromeLauncher.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await orchestrator.LoginAsync(
                    "TestProfile",
                    ProviderKind.Codex,
                    null!,
                    TimeSpan.FromMinutes(1),
                    CancellationToken.None);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoginAsync_DirectLogin_does_not_invoke_google_authentication_service()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vaultPaths = new GoogleAccountVaultPaths(tempDir);
            var googleVault = new GoogleAccountVaultStore(vaultPaths);
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            await providerVault.SaveConnectionAsync(new RouterPlus.Core.Models.ProviderAuthConnection
            {
                ProfileName = "TestProfile",
                Provider = ProviderKind.GitHub,
                PreferredMethod = AuthMethod.Direct,
                DirectCredential = new RouterPlus.Core.Models.ProviderCredential
                {
                    Email = "direct@example.test",
                    Password = "synthetic-password",
                    TotpSecret = "NONE"
                }
            });

            var googleService = new Mock<IGoogleAuthenticationService>();
            var launcher = new Mock<IChromeLauncher>();
            launcher.Setup(item => item.LaunchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Uri>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((CdpSession?)null);

            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                launcher.Object,
                googleAuthenticationService: googleService.Object);

            var result = await orchestrator.LoginAsync(
                "TestProfile",
                ProviderKind.GitHub,
                new Uri("https://github.com/login"),
                TimeSpan.FromMinutes(1),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(AuthMethod.Direct, result.Method);
            Assert.Contains("launch browser", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            googleService.Verify(
                service => service.AuthenticateAsync(
                    It.IsAny<GoogleAuthenticationRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Constructor_NullGoogleVault_ThrowsArgumentNullException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            var mockChromeLauncher = new Mock<IChromeLauncher>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                new AutoLoginOrchestrator(
                    null!,
                    providerVault,
                    mockChromeLauncher.Object);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Constructor_NullProviderVault_ThrowsArgumentNullException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vaultPaths = new GoogleAccountVaultPaths(tempDir);
            var googleVault = new GoogleAccountVaultStore(vaultPaths);
            var mockChromeLauncher = new Mock<IChromeLauncher>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                new AutoLoginOrchestrator(
                    googleVault,
                    null!,
                    mockChromeLauncher.Object);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Constructor_NullChromeLauncher_ThrowsArgumentNullException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vaultPaths = new GoogleAccountVaultPaths(tempDir);
            var googleVault = new GoogleAccountVaultStore(vaultPaths);
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                new AutoLoginOrchestrator(
                    googleVault,
                    providerVault,
                    null!);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

