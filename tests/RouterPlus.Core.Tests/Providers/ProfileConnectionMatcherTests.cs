using System;
using System.Collections.Generic;
using System.Linq;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class ProfileConnectionMatcherTests
{
    private static ChromeProfile MakeProfile(string id, string name, string directoryName = "Default")
        => new ChromeProfile(id, name, directoryName, $"C:\\Chrome\\User Data\\{directoryName}", false);

    private static ProviderConnection MakeConnection(
        string id,
        ProviderKind provider,
        string? name,
        int priority,
        bool isActive = true,
        string? email = null,
        DateTimeOffset? createdAt = null)
        => new ProviderConnection(id, provider, name, priority, isActive, email, createdAt);

    private static ChromeProfile[] MakeAllProfiles(params ChromeProfile[] profiles) => profiles;

    // ----- MatchConnections tests -----

    [Fact]
    public void MatchConnections_exact_name_and_provider_match_returns_connection()
    {
        var profile = MakeProfile("p1", "Work");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1),
            MakeConnection("c2", ProviderKind.Codex, "Personal", 2),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Single(matches);
        Assert.Equal("c1", matches[0].Id);
    }

    [Fact]
    public void MatchConnections_wrong_provider_ignored()
    {
        var profile = MakeProfile("p1", "Work");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Kiro, "Work", 1),
            MakeConnection("c2", ProviderKind.Codex, "Personal", 2),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Empty(matches);
    }

    [Fact]
    public void MatchConnections_connection_email_present_and_differs_from_profile_email_no_match()
    {
        var profile = MakeProfile("p1", "user@example.com");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "user@example.com", 1, email: "other@example.com"),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Empty(matches);
    }

    [Fact]
    public void MatchConnections_connection_email_equals_profile_email_case_insensitive_match()
    {
        var profile = MakeProfile("p1", "USER@EXAMPLE.COM");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "user@example.com", 1, email: "user@example.com"),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Single(matches);
        Assert.Equal("c1", matches[0].Id);
    }

    [Fact]
    public void MatchConnections_connection_email_empty_falls_back_to_name_match()
    {
        var profile = MakeProfile("p1", "Work");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1, email: ""),
            MakeConnection("c2", ProviderKind.Codex, "Work", 2, email: null),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Equal(2, matches.Count);
        Assert.Equal("c1", matches[0].Id); // lower priority first
        Assert.Equal("c2", matches[1].Id);
    }

    [Fact]
    public void MatchConnections_profile_name_not_email_skips_email_check()
    {
        var profile = MakeProfile("p1", "Work"); // not email-like
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1, email: "different@example.com"),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Single(matches);
        Assert.Equal("c1", matches[0].Id);
    }

    [Fact]
    public void MatchConnections_duplicate_profile_names_returns_empty()
    {
        var profile1 = MakeProfile("p1", "Work");
        var profile2 = MakeProfile("p2", "Work"); // same normalized name
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1),
        };
        var allProfiles = MakeAllProfiles(profile1, profile2);

        var matches = ProfileConnectionMatcher.MatchConnections(profile1, ProviderKind.Codex, connections, allProfiles);

        Assert.Empty(matches);
    }

    [Fact]
    public void MatchConnections_null_or_whitespace_profile_name_ignored()
    {
        var profile = MakeProfile("p1", "   ");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Empty(matches);
    }

    [Fact]
    public void MatchConnections_deterministic_sort_priority_asc_createdAt_asc_null_last_id_ordinal()
    {
        var profile = MakeProfile("p1", "Work");
        var early = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);

        // Input order is shuffled: priority 2 first, then priority 1 with different CreatedAt, then priority 1 same CreatedAt different Id
        var connections = new[]
        {
            MakeConnection("z-last", ProviderKind.Codex, "Work", 2, createdAt: early), // priority 2
            MakeConnection("a-first-priority", ProviderKind.Codex, "Work", 1, createdAt: late), // priority 1, later CreatedAt
            MakeConnection("b-mid-priority", ProviderKind.Codex, "Work", 1, createdAt: early), // priority 1, earlier CreatedAt
            MakeConnection("c-same-priority-created", ProviderKind.Codex, "Work", 1, createdAt: early), // same priority & CreatedAt, id comes after b
            MakeConnection("d-null-createdat", ProviderKind.Codex, "Work", 1, createdAt: null), // null CreatedAt should be last
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Equal(5, matches.Count);
        // Expected order: b (pri 1, early), c (pri 1, early, id b < c), a (pri 1, late), d (pri 1, null CreatedAt), z (pri 2)
        Assert.Equal("b-mid-priority", matches[0].Id);
        Assert.Equal("c-same-priority-created", matches[1].Id);
        Assert.Equal("a-first-priority", matches[2].Id);
        Assert.Equal("d-null-createdat", matches[3].Id);
        Assert.Equal("z-last", matches[4].Id);
    }

    [Fact]
    public void MatchConnections_throws_when_null_arguments()
    {
        var profile = MakeProfile("p1", "Work");
        var connections = new[] { MakeConnection("c1", ProviderKind.Codex, "Work", 1) };
        var allProfiles = MakeAllProfiles(profile);

        Assert.Throws<ArgumentNullException>(() => ProfileConnectionMatcher.MatchConnections(null!, ProviderKind.Codex, connections, allProfiles));
        Assert.Throws<ArgumentNullException>(() => ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, null!, allProfiles));
        Assert.Throws<ArgumentNullException>(() => ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, null!));
    }

    // ----- CountByProvider tests (existing) -----

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

    // ----- MatchAllProviders tests -----

    [Fact]
    public void MatchAllProviders_returns_matches_for_all_providers()
    {
        var profile = MakeProfile("p1", "Work");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1),
            MakeConnection("c2", ProviderKind.OpenRouter, "Work", 1),
            MakeConnection("c3", ProviderKind.Kiro, "Other", 1),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchAllProviders(profile, connections, allProfiles);

        Assert.Single(matches[ProviderKind.Codex]);
        Assert.Single(matches[ProviderKind.OpenRouter]);
        Assert.Empty(matches[ProviderKind.Kiro]);
    }

    [Fact]
    public void MatchAllProviders_ambiguous_name_returns_empty_for_all()
    {
        var profile1 = MakeProfile("p1", "Work");
        var profile2 = MakeProfile("p2", "Work");
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1),
        };
        var allProfiles = MakeAllProfiles(profile1, profile2);

        var matches = ProfileConnectionMatcher.MatchAllProviders(profile1, connections, allProfiles);

        Assert.All(ProviderCatalog.All, definition => Assert.Empty(matches[definition.Kind]));
    }

    // ----- GetAmbiguousProfileNames tests -----

    [Fact]
    public void GetAmbiguousProfileNames_returns_duplicate_normalized_names()
    {
        var profiles = new[]
        {
            MakeProfile("p1", "Work"),
            MakeProfile("p2", "WORK"),
            MakeProfile("p3", "  Work  "),
            MakeProfile("p4", "Personal"),
        };

        var ambiguous = ProfileConnectionMatcher.GetAmbiguousProfileNames(profiles);

        Assert.Single(ambiguous);
        Assert.Contains("work", ambiguous);
    }

    [Fact]
    public void GetAmbiguousProfileNames_returns_empty_when_no_duplicates()
    {
        var profiles = new[]
        {
            MakeProfile("p1", "Work"),
            MakeProfile("p2", "Personal"),
        };

        var ambiguous = ProfileConnectionMatcher.GetAmbiguousProfileNames(profiles);

        Assert.Empty(ambiguous);
    }

    [Fact]
    public void GetAmbiguousProfileNames_throws_when_null()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileConnectionMatcher.GetAmbiguousProfileNames(null!));
    }

    // ----- New tests for case-insensitive ambiguity and whitespace handling -----

    [Fact]
    public void MatchConnections_case_only_duplicate_names_returns_empty_for_both()
    {
        var profile1 = MakeProfile("p1", "Work");
        var profile2 = MakeProfile("p2", "work"); // case-only difference
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Work", 1),
        };
        var allProfiles = MakeAllProfiles(profile1, profile2);

        var matches1 = ProfileConnectionMatcher.MatchConnections(profile1, ProviderKind.Codex, connections, allProfiles);
        var matches2 = ProfileConnectionMatcher.MatchConnections(profile2, ProviderKind.Codex, connections, allProfiles);

        Assert.Empty(matches1);
        Assert.Empty(matches2);
    }

    [Fact]
    public void MatchConnections_case_only_duplicate_email_like_names_returns_empty_for_both()
    {
        var profile1 = MakeProfile("p1", "User@Example.com");
        var profile2 = MakeProfile("p2", "user@example.com"); // case-only difference
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "user@example.com", 1, email: "user@example.com"),
        };
        var allProfiles = MakeAllProfiles(profile1, profile2);

        var matches1 = ProfileConnectionMatcher.MatchConnections(profile1, ProviderKind.Codex, connections, allProfiles);
        var matches2 = ProfileConnectionMatcher.MatchConnections(profile2, ProviderKind.Codex, connections, allProfiles);

        Assert.Empty(matches1);
        Assert.Empty(matches2);
    }

    [Fact]
    public void MatchConnections_profile_name_with_whitespace_matches_connection_email()
    {
        var profile = MakeProfile("p1", "  user@example.com  "); // surrounding whitespace
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "user@example.com", 1, email: "user@example.com"),
        };
        var allProfiles = MakeAllProfiles(profile);

        var matches = ProfileConnectionMatcher.MatchConnections(profile, ProviderKind.Codex, connections, allProfiles);

        Assert.Single(matches);
        Assert.Equal("c1", matches[0].Id);
    }
}
