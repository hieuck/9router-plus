using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests.Api;

public sealed class OAuthCallbackListenerTests
{
    [Fact]
    public async Task WaitForCallback_times_out_with_a_descriptive_exception()
    {
        await using var listener = await OAuthCallbackListener.StartAsync();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            listener.WaitForCallbackAsync(TimeSpan.FromMilliseconds(25)));
    }

    [Fact]
    public void ParseCallbackUri_reads_error_details()
    {
        var callback = OAuthCallbackListener.ParseCallbackUri(new Uri(
            "http://127.0.0.1:38579/callback?error=access_denied&error_description=User+denied+access"));

        Assert.Null(callback.Code);
        Assert.Null(callback.Token);
        Assert.Equal("access_denied", callback.Error);
        Assert.Equal("User denied access", callback.ErrorDescription);
    }

    [Fact]
    public void ParseCallbackUri_merges_fragment_values_over_query_values()
    {
        var callback = OAuthCallbackListener.ParseCallbackUri(new Uri(
            "http://127.0.0.1:38579/callback?code=query-code&state=query-state#code=fragment-code&token=fragment-token"));

        Assert.Equal("fragment-code", callback.Code);
        Assert.Equal("fragment-token", callback.Token);
        Assert.Equal("query-state", callback.State);
    }

    [Fact]
    public void ParseCallbackUri_decodes_encoded_values_and_supports_key_without_value()
    {
        var callback = OAuthCallbackListener.ParseCallbackUri(new Uri(
            "http://127.0.0.1:38579/callback?code=hello%20world&state"));

        Assert.Equal("hello world", callback.Code);
        Assert.Equal(string.Empty, callback.State);
    }

    [Fact]
    public void ParseCallbackUri_throws_for_null_uri()
    {
        Assert.Throws<ArgumentNullException>(() => OAuthCallbackListener.ParseCallbackUri(null!));
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
