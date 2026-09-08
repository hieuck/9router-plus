using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class OpenRouterOnboardingResultTests
{
    [Fact]
    public void Succeeded_creates_success_result_with_api_key()
    {
        var result = OpenRouterOnboardingResult.Succeeded("sk-or-v1-test");

        Assert.True(result.Success);
        Assert.Equal("sk-or-v1-test", result.ApiKey);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Failed_creates_failure_result_with_error_message()
    {
        var result = OpenRouterOnboardingResult.Failed("authorization was denied");

        Assert.False(result.Success);
        Assert.Null(result.ApiKey);
        Assert.Equal("authorization was denied", result.ErrorMessage);
    }

    [Fact]
    public void Results_are_value_equal_when_their_fields_match()
    {
        var first = OpenRouterOnboardingResult.Succeeded("sk-or-v1-test");
        var second = new OpenRouterOnboardingResult(true, "sk-or-v1-test", null);

        Assert.Equal(first, second);
    }
}
