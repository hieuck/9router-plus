using Moq;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
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
    public async Task LoginAsync_DirectPrimaryFallsBackToGoogleOAuth_WhenOAuthSucceeds()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        try
        {
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            await providerVault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = "TestProfile",
                Provider = ProviderKind.GitHub,
                PreferredMethod = AuthMethod.Direct,
                LinkedGoogleAccount = "google@example.test",
                DirectCredential = new ProviderCredential
                {
                    Email = "direct@example.test",
                    Password = "synthetic-password"
                }
            });

            var googleVault = new FakeGoogleAccountVaultStore(new GoogleAccountVault(
                new[] { new GoogleLoginCredential("google-profile", "google@example.test", "synthetic-password", "NONE") }));
            var launcher = new QueueChromeLauncher(null, CreateCdpSession());
            var adapter = new FakeOAuthAdapter(ProviderKind.GitHub, new ProviderOAuthResult(true, false, "authorized"));
            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                launcher,
                new ProviderOAuthAdapterRegistry(adapter),
                new Mock<IGoogleAuthenticationService>().Object);

            // Act
            var result = await orchestrator.LoginAsync(
                "TestProfile", ProviderKind.GitHub, new Uri("https://fallback.test"),
                TimeSpan.FromMinutes(1), CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(AuthMethod.GoogleOAuth, result.Method);
            Assert.Null(result.ErrorMessage);
            Assert.Equal(new Uri("https://github.com/login"), launcher.LaunchedUrls[0]);
            Assert.Equal(new Uri("https://github.com/login"), launcher.LaunchedUrls[1]);
            Assert.Equal(1, adapter.CallCount);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoginAsync_GooglePrimaryReturnsAccountNotFound_WhenLinkedAccountIsMissing()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        try
        {
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            await providerVault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = "TestProfile",
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "missing@example.test"
            });

            var googleVault = new FakeGoogleAccountVaultStore(new GoogleAccountVault());
            var launcher = new Mock<IChromeLauncher>();
            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                launcher.Object);

            // Act
            var result = await orchestrator.LoginAsync(
                "TestProfile", ProviderKind.Codex, new Uri("https://fallback.test"),
                TimeSpan.FromMinutes(1), CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(AuthMethod.GoogleOAuth, result.Method);
            Assert.Contains("not found in vault", result.ErrorMessage);
            launcher.Verify(item => item.LaunchAsync(
                It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoginAsync_GooglePrimaryFallsBackToDirect_WhenVaultIsLocked()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        try
        {
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            await providerVault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = "TestProfile",
                Provider = ProviderKind.OpenRouter,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "google@example.test",
                DirectCredential = new ProviderCredential
                {
                    Email = "direct@example.test",
                    Password = "synthetic-password"
                }
            });

            var googleVault = new FakeGoogleAccountVaultStore(new GoogleAccountVault(), rememberedSession: null, returnRememberedSession: false);
            var launcher = new Mock<IChromeLauncher>();
            launcher.Setup(item => item.LaunchAsync(
                    It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CdpSession?)null);
            var orchestrator = new AutoLoginOrchestrator(googleVault, providerVault, launcher.Object);

            // Act
            var result = await orchestrator.LoginAsync(
                "TestProfile", ProviderKind.OpenRouter, new Uri("https://fallback.test"),
                TimeSpan.FromMinutes(1), CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(AuthMethod.GoogleOAuth, result.Method);
            Assert.Contains("not unlocked", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            launcher.Verify(item => item.LaunchAsync(
                "TestProfile", new Uri("https://openrouter.ai/"), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoginAsync_GoogleVaultCancellation_PropagatesCancellation()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            await providerVault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = "TestProfile",
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "google@example.test"
            });

            using var cancellation = new CancellationTokenSource();
            var expected = new OperationCanceledException(cancellation.Token);
            var googleVault = new FakeGoogleAccountVaultStore(
                new GoogleAccountVault(),
                exception: expected);
            var orchestrator = new AutoLoginOrchestrator(
                googleVault,
                providerVault,
                new Mock<IChromeLauncher>().Object);

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                orchestrator.LoginAsync(
                    "TestProfile",
                    ProviderKind.Codex,
                    new Uri("https://fallback.test"),
                    TimeSpan.FromMinutes(1),
                    cancellation.Token));

            Assert.Same(expected, exception);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoginAsync_DirectPrimaryWithoutFallback_DoesNotRetry()
    {
        // Arrange
        var tempDir = CreateTempDirectory();
        try
        {
            var providerVault = new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.json"));
            await providerVault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = "TestProfile",
                Provider = ProviderKind.Kiro,
                PreferredMethod = AuthMethod.Direct,
                DirectCredential = new ProviderCredential
                {
                    Email = "direct@example.test",
                    Password = "synthetic-password"
                }
            });

            var launcher = new Mock<IChromeLauncher>();
            launcher.Setup(item => item.LaunchAsync(
                    It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CdpSession?)null);
            var orchestrator = new AutoLoginOrchestrator(
                new FakeGoogleAccountVaultStore(new GoogleAccountVault()), providerVault, launcher.Object);

            // Act
            var result = await orchestrator.LoginAsync(
                "TestProfile", ProviderKind.Kiro, new Uri("https://fallback.test"),
                TimeSpan.FromMinutes(1), CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(AuthMethod.Direct, result.Method);
            launcher.Verify(item => item.LaunchAsync(
                "TestProfile", new Uri("https://kiro.dev/"), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static CdpSession CreateCdpSession()
    {
        return new CdpSession(new ChromeCdpClient(new Uri("http://127.0.0.1:9222")), "session", "target");
    }

    private sealed class FakeGoogleAccountVaultStore : IGoogleAccountVaultStore
    {
        private readonly GoogleAccountVaultSession? _rememberedSession;
        private readonly GoogleAccountVault _vault;
        private readonly bool _returnRememberedSession;
        private readonly Exception? _exception;

        public FakeGoogleAccountVaultStore(
            GoogleAccountVault vault,
            GoogleAccountVaultSession? rememberedSession = null,
            bool returnRememberedSession = true,
            Exception? exception = null)
        {
            _vault = vault;
            _rememberedSession = rememberedSession;
            _returnRememberedSession = returnRememberedSession;
            _exception = exception;
        }

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                return Task.FromException<GoogleAccountVaultSession?>(_exception);
            }

            return Task.FromResult(_returnRememberedSession ? _rememberedSession ?? new FakeSession(_vault) : null);
        }

        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private sealed class FakeSession : GoogleAccountVaultSession
        {
            public FakeSession(GoogleAccountVault vault) => Vault = vault;
            public string VaultId => "test-vault";
            public GoogleAccountVault Vault { get; private set; }
            public void Replace(GoogleAccountVault vault) => Vault = vault;
            public Task RememberAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveRememberedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class QueueChromeLauncher : IChromeLauncher
    {
        private readonly Queue<CdpSession?> _sessions;
        public List<Uri> LaunchedUrls { get; } = new();

        public QueueChromeLauncher(params CdpSession?[] sessions) => _sessions = new(sessions);

        public Task<CdpSession?> LaunchAsync(string profileName, Uri loginUrl, CancellationToken cancellationToken)
        {
            LaunchedUrls.Add(loginUrl);
            return Task.FromResult(_sessions.Dequeue());
        }
    }

    private sealed class FakeOAuthAdapter : IProviderOAuthAdapter
    {
        private readonly ProviderOAuthResult _result;
        public FakeOAuthAdapter(ProviderKind provider, ProviderOAuthResult result)
        {
            Provider = provider;
            _result = result;
        }
        public ProviderKind Provider { get; }
        public int CallCount { get; private set; }
        public Task<ProviderOAuthResult> RunAsync(ProviderOAuthRequest request, IGoogleAuthenticationService googleAuthentication, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
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

