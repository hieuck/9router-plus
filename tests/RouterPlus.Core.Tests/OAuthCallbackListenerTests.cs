using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

public sealed class OAuthCallbackListenerTests
{
    [Fact]
    public async Task WaitForCallback_times_out_with_a_descriptive_exception()
    {
        await using var listener = await OAuthCallbackListener.StartAsync();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            listener.WaitForCallbackAsync(TimeSpan.FromMilliseconds(25)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:38579/callback?code=abc&state=xyz", "abc", "", "xyz")]
    [InlineData("http://127.0.0.1:38579/callback?token=kimchi-token&state=xyz", "", "kimchi-token", "xyz")]
    public void ParseCallbackUri_reads_code_or_token_and_state(
        string callbackUrl,
        string expectedCode,
        string expectedToken,
        string expectedState)
    {
        var callback = OAuthCallbackListener.ParseCallbackUri(new Uri(callbackUrl));

        Assert.Equal(expectedCode, callback.Code ?? string.Empty);
        Assert.Equal(expectedToken, callback.Token ?? string.Empty);
        Assert.Equal(expectedState, callback.State);
        Assert.Null(callback.Error);
    }

    [Theory]
    [InlineData("expected-state", "expected-state", true)]
    [InlineData("unexpected-state", "expected-state", false)]
    [InlineData(null, "expected-state", false)]
    public void Callback_state_must_match_the_authorization_session(
        string? callbackState,
        string expectedState,
        bool expectedMatch)
    {
        var callback = new OAuthCallbackData("code", null, callbackState, null, null);

        Assert.Equal(expectedMatch, callback.MatchesState(expectedState));
    }
}
