using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class ProfileConnectionMatcherTests
{
    [Fact]
    public void CountByProvider_matches_connection_names_case_insensitively_and_ignores_null_names()
    {
        var profile = new ChromeProfile("profile-1", "Work", "Profile 1", "C:\\Chrome\\User Data", false);
        var connections = new[]
        {
            new ProviderConnection("codex-1", ProviderKind.Codex, "work", 1, true),
            new ProviderConnection("codex-2", ProviderKind.Codex, "Work", 2, false),
            new ProviderConnection("codex-3", ProviderKind.Codex, null, 3, true),
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

    [Fact]
    public void CountByProvider_returns_zero_for_every_provider_without_matching_connections()
    {
        var profile = new ChromeProfile("profile-1", "Work", "Profile 1", "C:\\Chrome\\User Data", false);

        var counts = ProfileConnectionMatcher.CountByProvider(
            profile,
            new[] { new ProviderConnection("other", ProviderKind.Codex, "Personal", 1, true) });

        Assert.All(ProviderCatalog.All, definition => Assert.Equal(0, counts[definition.Kind]));
    }

    [Fact]
    public void CountByProvider_throws_when_profile_or_connections_are_null()
    {
        var profile = new ChromeProfile("profile-1", "Work", "Profile 1", "C:\\Chrome\\User Data", false);

        Assert.Throws<ArgumentNullException>(() => ProfileConnectionMatcher.CountByProvider(null!, Array.Empty<ProviderConnection>()));
        Assert.Throws<ArgumentNullException>(() => ProfileConnectionMatcher.CountByProvider(profile, null!));
    }

    [Fact]
    public void CountByProvider_trims_profile_name_before_matching()
    {
        var profile = new ChromeProfile("profile-1", "  Work  ", "Profile 1", "C:\\Chrome\\User Data", false);

        var counts = ProfileConnectionMatcher.CountByProvider(
            profile,
            new[] { new ProviderConnection("codex-1", ProviderKind.Codex, "work", 1, true) });

        Assert.Equal(1, counts[ProviderKind.Codex]);
    }
}
