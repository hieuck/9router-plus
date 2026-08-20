using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class ProfileSecretKeyTests
{
    [Fact]
    public void Create_is_stable_for_the_same_profile_and_provider()
    {
        var profile = new ChromeProfile("profile-id", "Work", "Profile 3", "C:\\Chrome\\User Data", false);

        var first = ProfileSecretKey.Create(profile, ProviderKind.OpenRouter);
        var second = ProfileSecretKey.Create(profile, ProviderKind.OpenRouter);

        Assert.Equal(first, second);
        Assert.StartsWith("openrouter-", first);
    }

    [Fact]
    public void Create_changes_when_the_provider_changes()
    {
        var profile = new ChromeProfile("profile-id", "Work", "Profile 3", "C:\\Chrome\\User Data", false);

        var openRouter = ProfileSecretKey.Create(profile, ProviderKind.OpenRouter);
        var ollama = ProfileSecretKey.Create(profile, ProviderKind.Ollama);

        Assert.NotEqual(openRouter, ollama);
    }
}
