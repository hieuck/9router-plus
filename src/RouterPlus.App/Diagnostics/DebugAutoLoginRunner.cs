using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using WpfApplication = System.Windows.Application;

namespace RouterPlus.App.Diagnostics;

/// <summary>
/// Debug-only runner that executes Google Auto Login automation without UI,
/// activated by ROUTERPLUS_DEBUG_AUTOLOGIN=1. Validates the production flow
/// end-to-end without FlaUI flakiness.
/// </summary>
internal static class DebugAutoLoginRunner
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Debug Auto Login Runner ===");
        Console.WriteLine($"Time: {DateTimeOffset.UtcNow:O}");

        try
        {
            var profileName = Environment.GetEnvironmentVariable("ROUTERPLUS_LIVE_PROFILE");
            if (string.IsNullOrWhiteSpace(profileName))
            {
                Console.WriteLine("ERROR: ROUTERPLUS_LIVE_PROFILE not set.");
                WpfApplication.Current.Shutdown(1);
                return;
            }

            Console.WriteLine($"Profile: {profileName}");

            // Load settings
            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load();

            if (string.IsNullOrWhiteSpace(settings.ChromeExecutablePath) ||
                string.IsNullOrWhiteSpace(settings.ChromeUserDataDirectory))
            {
                Console.WriteLine("ERROR: Chrome not configured.");
                WpfApplication.Current.Shutdown(1);
                return;
            }

            Console.WriteLine($"Chrome: {settings.ChromeExecutablePath}");
            Console.WriteLine($"UserData: {settings.ChromeUserDataDirectory}");

            // Discover profiles
            var profileReader = new ChromeProfileReader();
            var discoveredProfiles = profileReader.Read(settings.ChromeUserDataDirectory)
                .Where(p => Directory.Exists(p.ProfilePath))
                .ToList();

            var profiles = ChromeProfileCatalog.Merge(
                discoveredProfiles,
                Array.Empty<ManagedChromeProfile>(),
                settings.ChromeUserDataDirectory);

            Console.WriteLine($"Found {profiles.Count} profile(s).");

            // Search by Name or DirectoryName
            var profile = profiles.FirstOrDefault(p => p.Name == profileName)
                ?? profiles.FirstOrDefault(p => p.DirectoryName == profileName);

            if (profile == null)
            {
                Console.WriteLine($"ERROR: Profile '{profileName}' not found by Name or DirectoryName.");
                Console.WriteLine("Available profiles (first 20):");
                foreach (var p in profiles.Take(20))
                {
                    Console.WriteLine($"  Name='{p.Name}' DirName='{p.DirectoryName}'");
                }
                WpfApplication.Current.Shutdown(1);
                return;
            }

            Console.WriteLine($"Selected profile: {profile.DirectoryName}");

            // Open vault
            var vaultPaths = new GoogleAccountVaultPaths();
            var vaultStore = new GoogleAccountVaultStore(vaultPaths);
            var vaultPath = vaultPaths.VaultPath;

            Console.WriteLine($"Vault: {vaultPath}");

            if (!File.Exists(vaultPath))
            {
                Console.WriteLine("ERROR: Vault does not exist. Run the dialog once to create it.");
                WpfApplication.Current.Shutdown(1);
                return;
            }

            GoogleAccountVaultSession? session = null;
            try
            {
                session = await vaultStore.TryOpenRememberedAsync(vaultPath, CancellationToken.None);
            }
            catch
            {
                // Fall through
            }

            if (session == null)
            {
                Console.WriteLine("ERROR: Vault not remembered. Unlock it once in the dialog with 'Remember'.");
                WpfApplication.Current.Shutdown(1);
                return;
            }

            Console.WriteLine("Vault unlocked from remembered device.");

            var credential = session.Vault.Find(profile.Id);
            if (credential == null)
            {
                Console.WriteLine($"ERROR: No credential for profile {profile.Id} in vault.");
                await session.DisposeAsync();
                WpfApplication.Current.Shutdown(1);
                return;
            }

            Console.WriteLine($"Credential found: email={credential.Email}");

            // Run automation
            var launcher = new ChromeLauncher();
            var installation = new ChromeInstallation(settings.ChromeExecutablePath, settings.ChromeUserDataDirectory);

            Console.WriteLine("Starting automation...");

            ChromeManagedSession? managedSession = null;
            IGoogleLoginBrowser? browser = null;

            try
            {
                managedSession = await launcher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://accounts.google.com/"),
                    CancellationToken.None);

                Console.WriteLine($"Chrome launched: PID={managedSession.Process.Id}");
                Console.WriteLine("Waiting 3 seconds for page load...");
                await Task.Delay(3000);

                browser = await managedSession.ConnectGoogleLoginAsync(CancellationToken.None);
                Console.WriteLine("CDP connected.");

                // Check if already logged in via session cookies
                var initialState = await browser.ReadStateAsync(CancellationToken.None);
                if (initialState.HasCompletionSignal && initialState.PageUri.Host == "myaccount.google.com")
                {
                    Console.WriteLine("Result: Success (session cookies auto-login)");
                    Console.WriteLine($"Current URL: {initialState.PageUri}");
                    Console.WriteLine("Message: Already logged in via existing session.");
                    Console.WriteLine("SUCCESS: Auto Login completed.");
                    WpfApplication.Current.Shutdown(0);
                    return;
                }

                Console.WriteLine("Session cookies did not authenticate. Starting automation flow...");

                var result = await GoogleLoginStateMachine.RunAsync(
                    browser,
                    credential,
                    CancellationToken.None);

                Console.WriteLine($"Result: {result.Category}");
                Console.WriteLine($"Message: {result.Message}");

                if (result.Category == GoogleLoginResultCategory.Success)
                {
                    Console.WriteLine("SUCCESS: Auto Login completed.");
                    WpfApplication.Current.Shutdown(0);
                }
                else
                {
                    Console.WriteLine("FAILED: Auto Login did not complete.");
                    WpfApplication.Current.Shutdown(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                WpfApplication.Current.Shutdown(1);
            }
            finally
            {
                if (browser != null)
                {
                    await browser.DisposeAsync();
                }
                if (managedSession != null)
                {
                    // Keep Chrome open for 5 seconds so user can see the result.
                    Console.WriteLine("Waiting 5 seconds before cleanup...");
                    await Task.Delay(5000);
                    await managedSession.DisposeAsync();
                }
                await session.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            WpfApplication.Current.Shutdown(1);
        }
    }
}
