using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleLoginCdpBrowserTests
{
    [Fact]
    public async Task ReadStateAsync_reads_target_and_runtime_state_from_fake_cdp()
    {
        var cdp = new FakeChromeCdpClient();
        cdp.Enqueue("Target.getTargets", Targets("https://accounts.google.com/signin"));
        cdp.Enqueue("Runtime.evaluate", PageState("https://accounts.google.com/signin", hasEmailField: true), "session-1");
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        var state = await browser.ReadStateAsync(CancellationToken.None);

        Assert.Equal(new Uri("https://accounts.google.com/signin"), state.PageUri);
        Assert.True(state.HasEmailField);
        Assert.False(state.HasPasswordField);
        Assert.False(state.HasTotpField);
        Assert.False(state.HasCompletionSignal);
        Assert.False(state.HasManualChallenge);
        cdp.AssertComplete();
    }

    [Fact]
    public async Task ReadStateAsync_rejects_target_that_navigated_to_unauthorized_host()
    {
        var cdp = new FakeChromeCdpClient();
        cdp.Enqueue("Target.getTargets", Targets("https://evil.example/login"));
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => browser.ReadStateAsync(CancellationToken.None));

        Assert.Contains("unauthorized host", ex.Message);
        cdp.AssertComplete();
    }

    [Fact]
    public async Task ReadStateAsync_rejects_missing_target()
    {
        var cdp = new FakeChromeCdpClient();
        cdp.Enqueue("Target.getTargets", Json("{\"targetInfos\":[]}"));
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => browser.ReadStateAsync(CancellationToken.None));

        Assert.Contains("closed", ex.Message);
        cdp.AssertComplete();
    }

    [Fact]
    public async Task ReadStateAsync_wraps_malformed_runtime_state()
    {
        var cdp = new FakeChromeCdpClient();
        cdp.Enqueue("Target.getTargets", Targets("https://accounts.google.com/signin"));
        cdp.Enqueue("Runtime.evaluate", Json("{\"result\":{\"value\":{\"pageUrl\":\"not-a-uri\"}}}"), "session-1");
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => browser.ReadStateAsync(CancellationToken.None));

        Assert.Contains("Failed to read page state", ex.Message);
        cdp.AssertComplete();
    }

    [Fact]
    public void IsManualChallenge_detects_recaptcha_path_case_insensitively()
    {
        Assert.True(GoogleLoginCdpBrowser.IsManualChallenge(
            new Uri("https://accounts.google.com/v3/signin/CHALLENGE/RECAPTCHA"),
            hasChallengeElement: false));
    }

    [Fact]
    public void IsManualChallenge_detects_challenge_element_and_rejects_null_uri()
    {
        Assert.True(GoogleLoginCdpBrowser.IsManualChallenge(
            new Uri("https://accounts.google.com/signin/challenge/verify"),
            hasChallengeElement: true));
        Assert.False(GoogleLoginCdpBrowser.IsManualChallenge(
            new Uri("https://accounts.google.com/signin/v2/identifier"),
            hasChallengeElement: false));
        Assert.Throws<ArgumentNullException>(() => GoogleLoginCdpBrowser.IsManualChallenge(null!, false));
    }

    [Fact]
    public async Task FillAsync_rejects_blank_value_before_calling_cdp()
    {
        var cdp = new FakeChromeCdpClient();
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        await Assert.ThrowsAsync<ArgumentException>(() => browser.FillAsync(GoogleLoginField.Email, " ", CancellationToken.None));

        Assert.Empty(cdp.Calls);
    }

    [Fact]
    public async Task FillAsync_uses_fake_cdp_and_does_not_retain_secret_value()
    {
        const string syntheticSecret = "synthetic-password-value";
        var cdp = new FakeChromeCdpClient();
        cdp.Enqueue("Target.getTargets", Targets("https://accounts.google.com/signin"));
        cdp.Enqueue("Runtime.evaluate", FocusedField(), "session-1");
        cdp.Enqueue("Input.dispatchKeyEvent", EmptyResult(), "session-1");
        cdp.Enqueue("Input.dispatchKeyEvent", EmptyResult(), "session-1");
        cdp.Enqueue("Runtime.evaluate", EmptyValue(), "session-1");
        cdp.Enqueue("Input.insertText", EmptyResult(), "session-1");
        cdp.Enqueue("Runtime.evaluate", TriggeredValue(syntheticSecret.Length), "session-1");
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        await browser.FillAsync(GoogleLoginField.Password, syntheticSecret, CancellationToken.None);

        Assert.Contains(cdp.Calls, call => call.Method == "Input.insertText");
        Assert.DoesNotContain(cdp.Calls, call => call.ParametersText.Contains(syntheticSecret, StringComparison.Ordinal));
        cdp.AssertComplete();
    }

    [Fact]
    public async Task TrySelectAuthenticatorMethodAsync_returns_false_when_cancelled()
    {
        var cdp = new FakeChromeCdpClient();
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var selected = await browser.TrySelectAuthenticatorMethodAsync(cancellation.Token);

        Assert.False(selected);
        Assert.Empty(cdp.Calls);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent_and_blocks_future_operations()
    {
        var cdp = new FakeChromeCdpClient();
        await using var browser = new GoogleLoginCdpBrowser(cdp, "session-1", "target-1");

        await browser.DisposeAsync();
        await browser.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.ReadStateAsync(CancellationToken.None));
    }

    private static JsonElement Targets(string url)
        => Json($"{{\"targetInfos\":[{{\"targetId\":\"target-1\",\"url\":\"{url}\"}}]}}");

    private static JsonElement PageState(string url, bool hasEmailField)
        => Json($"{{\"result\":{{\"value\":{{\"pageUrl\":\"{url}\",\"hasEmailField\":{hasEmailField.ToString().ToLowerInvariant()},\"hasPasswordField\":false,\"hasTotpField\":false,\"hasTotpError\":false,\"has2FAMethodPicker\":false,\"hasCompletionSignal\":false,\"hasManualChallenge\":false}}}}}}");

    private static JsonElement FocusedField()
        => Json("{\"result\":{\"value\":{\"tagName\":\"INPUT\",\"type\":\"password\",\"name\":\"\",\"id\":\"password\",\"placeholder\":\"Password\"}}}");

    private static JsonElement EmptyResult()
        => Json("{\"result\":{}}");

    private static JsonElement EmptyValue()
        => Json("{\"result\":{\"value\":{\"value\":\"\",\"valueLength\":0}}}");

    private static JsonElement TriggeredValue(int length)
        => Json($"{{\"result\":{{\"value\":{{\"triggered\":true,\"finalValueLength\":{length}}}}}}}");

    private static JsonElement Json(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeChromeCdpClient : IChromeCdpClient
    {
        private readonly Queue<ExpectedCall> _expectedCalls = new();

        public List<(string Method, string ParametersText)> Calls { get; } = new();

        public void Enqueue(string method, JsonElement response, string? sessionId = null)
            => _expectedCalls.Enqueue(new ExpectedCall(method, sessionId, response));

        public Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken, string? sessionId = null)
        {
            if (_expectedCalls.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected CDP call: {method}.");
            }

            var expected = _expectedCalls.Dequeue();
            if (expected.Method != method || expected.SessionId != sessionId)
            {
                throw new InvalidOperationException(
                    $"Expected CDP call {expected.Method}/{expected.SessionId ?? "<none>"}, got {method}/{sessionId ?? "<none>"}.");
            }

            var parametersText = method == "Input.insertText"
                ? "<redacted>"
                : parameters is null ? string.Empty : JsonSerializer.Serialize(parameters);
            Calls.Add((method, parametersText));
            return Task.FromResult(expected.Response);
        }

        public void AssertComplete()
            => Assert.Empty(_expectedCalls);

        private sealed record ExpectedCall(string Method, string? SessionId, JsonElement Response);
    }
}
