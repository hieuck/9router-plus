using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="ManagedChromeTargetConnector"/> target selection logic.
/// These tests verify the connector's target selection policies without launching Chrome.
/// </summary>
public sealed class ManagedChromeTargetConnectorTests
{
    [Fact]
    public void SelectFirstPage_returns_first_page_target()
    {
        var targets = new List<ManagedChromeTarget>
        {
            new("target-1", "page", "https://example.com"),
            new("target-2", "page", "https://example.org"),
            new("target-3", "background_page", "chrome-extension://abc"),
        };

        var selected = ManagedChromeTargetConnector.SelectFirstPage(targets);

        Assert.Equal("target-1", selected);
    }

    [Fact]
    public void SelectFirstPage_returns_null_when_no_page_targets()
    {
        var targets = new List<ManagedChromeTarget>
        {
            new("target-1", "background_page", "chrome-extension://abc"),
            new("target-2", "service_worker", "chrome-extension://def"),
        };

        var selected = ManagedChromeTargetConnector.SelectFirstPage(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectMarkedGooglePage_selects_marked_target_over_unmarked()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("unmarked-1", "page", "https://accounts.google.com/signin"),
            new("marked-1", "page", $"https://accounts.google.com/signin#{sessionMarker}"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker);

        Assert.Equal("marked-1", selected);
    }

    [Fact]
    public void SelectMarkedGooglePage_fallbacks_to_single_google_target_when_no_marker()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("only-google", "page", "https://accounts.google.com/signin"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker);

        Assert.Equal("only-google", selected);
    }

    [Fact]
    public void SelectMarkedGooglePage_excludes_non_google_hosts()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("evil-1", "page", "https://evil.com/signin"),
            new("google-1", "page", "https://accounts.google.com/signin"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker);

        Assert.Equal("google-1", selected);
    }

    [Fact]
    public void SelectMarkedGooglePage_throws_on_multiple_marked_targets()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("marked-1", "page", $"https://accounts.google.com/signin#{sessionMarker}"),
            new("marked-2", "page", $"https://myaccount.google.com/profile#{sessionMarker}"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker));

        Assert.Contains("Multiple Google targets with session marker", ex.Message);
    }

    [Fact]
    public void SelectMarkedGooglePage_returns_null_when_no_google_targets()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("target-1", "page", "https://example.com"),
            new("target-2", "page", "https://example.org"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectMarkedGooglePage_allows_myaccount_google_com()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("target-1", "page", "https://myaccount.google.com/profile"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker);

        Assert.Equal("target-1", selected);
    }

    [Fact]
    public void SelectMarkedGooglePage_allows_www_google_com()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("target-1", "page", "https://www.google.com/"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker);

        Assert.Equal("target-1", selected);
    }

    // ===== SelectMarkedAgnesPage tests =====

    [Fact]
    public void SelectMarkedAgnesPage_selects_marked_target_over_unmarked()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("unmarked-1", "page", "https://platform.agnes-ai.com/settings/apiKeys"),
            new("marked-1", "page", $"https://platform.agnes-ai.com/settings/apiKeys#{sessionMarker}"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker);

        Assert.Equal("marked-1", selected);
    }

    [Fact]
    public void SelectMarkedAgnesPage_throws_when_no_marked_target()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("unmarked-1", "page", "https://platform.agnes-ai.com/settings/apiKeys"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker));

        Assert.Contains("No Agnes target with session marker found", ex.Message);
    }

    [Fact]
    public void SelectMarkedAgnesPage_throws_on_multiple_marked_targets()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("marked-1", "page", $"https://platform.agnes-ai.com/settings/apiKeys#{sessionMarker}"),
            new("marked-2", "page", $"https://platform.agnes-ai.com/login#{sessionMarker}"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker));

        Assert.Contains("Multiple Agnes targets with session marker found", ex.Message);
    }

    [Fact]
    public void SelectMarkedAgnesPage_excludes_non_agnes_hosts_throws_when_no_marked()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("evil-1", "page", "https://evil.com/settings/apiKeys"),
            new("agnes-1", "page", "https://platform.agnes-ai.com/settings/apiKeys"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker));

        Assert.Contains("No Agnes target with session marker found", ex.Message);
    }

    [Fact]
    public void SelectMarkedAgnesPage_excludes_background_pages_throws_when_no_marked()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("bg-1", "background_page", "chrome-extension://abc"),
            new("agnes-1", "page", "https://platform.agnes-ai.com/settings/apiKeys"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker));

        Assert.Contains("No Agnes target with session marker found", ex.Message);
    }

    [Fact]
    public void SelectMarkedAgnesPage_stale_first_page_plus_marked_tab_selects_marked()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("stale-1", "page", "about:blank"),
            new("agnes-unmarked", "page", "https://platform.agnes-ai.com/settings/apiKeys"),
            new("agnes-marked", "page", $"https://platform.agnes-ai.com/settings/apiKeys#{sessionMarker}"),
        };

        var selected = ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker);

        Assert.Equal("agnes-marked", selected);
    }

    [Fact]
    public void SelectMarkedAgnesPage_no_marker_throws_clear_error()
    {
        var sessionMarker = "__9rp_session_test123";
        var targets = new List<ManagedChromeTarget>
        {
            new("agnes-1", "page", "https://platform.agnes-ai.com/settings/apiKeys"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, sessionMarker));

        Assert.Contains("session marker found", ex.Message);
    }
}