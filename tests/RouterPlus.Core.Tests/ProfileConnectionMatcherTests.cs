using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class ProfileConnectionMatcherTests
{
    [Fact]
    public void CountByProvider_matches_connection_names_case_insensitively()
    {
        var profile = new ChromeProfile("profile-1", "Work", "Profile 1", "C:\\Chrome\\User Data", false);
        var connections = new[]
        {
            new ProviderConnection("codex-1", ProviderKind.Codex, "work", 1, true),
            new ProviderConnection("codex-2", ProviderKind.Codex, "Work", 2, false),
            new ProviderConnection("kiro-1", ProviderKind.Kiro, "Other", 1, true),
            new ProviderConnection("ollama-1", ProviderKind.Ollama, " Work ", 1, true)
        };

        var counts = ProfileConnectionMatcher.CountByProvider(profile, connections);

        Assert.Equal(2, counts[ProviderKind.Codex]);
        Assert.Equal(0, counts[ProviderKind.Kiro]);
        Assert.Equal(1, counts[ProviderKind.Ollama]);
        Assert.Equal(0, counts[ProviderKind.OpenRouter]);
        Assert.Equal(0, counts[ProviderKind.Kimchi]);
    }
}
