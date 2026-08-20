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
}
