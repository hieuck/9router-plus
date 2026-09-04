using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using Xunit;
using System.Diagnostics;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ProfileHealthServiceTests
{
    private static ChromeProfile CreateTestProfile()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        File.WriteAllText(Path.Combine(tempUserData, "Local State"), "{}");
        File.WriteAllText(Path.Combine(tempProfile, "Preferences"), "{}");
        File.WriteAllText(Path.Combine(tempProfile, "Secure Preferences"), "{}");

        return new ChromeProfile(
            "test-id",
            "Test Profile",
            "Profile 1",
            tempUserData,
            false);
    }

    [Fact]
    public async Task GetHealthStatusAsync_SecondCall_ReturnsCachedResult()
    {
        var profile = CreateTestProfile();
        var service = new ProfileHealthService();

        // First call - cache miss
        var status1 = await service.GetHealthStatusAsync(profile);

        // Second call - cache hit
        var stopwatch = Stopwatch.StartNew();
        var status2 = await service.GetHealthStatusAsync(profile);
        stopwatch.Stop();

        Assert.Same(status1, status2); // Same instance
        Assert.True(stopwatch.ElapsedMilliseconds < 50); // Sub-50ms

        Directory.Delete(profile.UserDataDirectory, true);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ForceRefresh_IgnoresCache()
    {
        var profile = CreateTestProfile();
        var service = new ProfileHealthService();

        var status1 = await service.GetHealthStatusAsync(profile);
        var status2 = await service.GetHealthStatusAsync(profile, forceRefresh: true);

        Assert.NotSame(status1, status2); // Different instances

        Directory.Delete(profile.UserDataDirectory, true);
    }

    [Fact]
    public async Task InvalidateCache_RemovesCachedEntry()
    {
        var profile = CreateTestProfile();
        var service = new ProfileHealthService();

        var status1 = await service.GetHealthStatusAsync(profile);
        service.InvalidateCache(profile);
        var status2 = await service.GetHealthStatusAsync(profile);

        Assert.NotSame(status1, status2); // Different instances after invalidation

        Directory.Delete(profile.UserDataDirectory, true);
    }

    [Fact]
    public async Task InvalidateAllCache_RemovesAllEntries()
    {
        var profile1 = CreateTestProfile();
        var profile2 = CreateTestProfile();
        var service = new ProfileHealthService();

        var status1 = await service.GetHealthStatusAsync(profile1);
        var status2 = await service.GetHealthStatusAsync(profile2);

        service.InvalidateAllCache();

        var status1After = await service.GetHealthStatusAsync(profile1);
        var status2After = await service.GetHealthStatusAsync(profile2);

        Assert.NotSame(status1, status1After);
        Assert.NotSame(status2, status2After);

        Directory.Delete(profile1.UserDataDirectory, true);
        Directory.Delete(profile2.UserDataDirectory, true);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ProfileWithoutGoogleAccount_ReturnsWarning()
    {
        var profile = CreateTestProfile();
        var vault = new GoogleAccountVault(Array.Empty<GoogleLoginCredential>());
        var service = new ProfileHealthService(vault);

        var status = await service.GetHealthStatusAsync(profile);

        Assert.Equal(HealthLevel.Warning, status.Level);
        Assert.Contains(status.Issues, i => i.Category == HealthCategory.Credentials);

        Directory.Delete(profile.UserDataDirectory, true);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ProfileWithGoogleAccount_Healthy()
    {
        var profile = CreateTestProfile();
        var credential = new GoogleLoginCredential(
            profile.Id,
            "test@gmail.com",
            "password123",
            "JBSWY3DPEHPK3PXP");
        var vault = new GoogleAccountVault(new[] { credential });
        var service = new ProfileHealthService(vault);

        var status = await service.GetHealthStatusAsync(profile);

        Assert.Equal(HealthLevel.Healthy, status.Level);

        Directory.Delete(profile.UserDataDirectory, true);
    }
}
