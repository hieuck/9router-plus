using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class CodexLoginResultTests
{
    [Fact]
    public void Success_has_success_category_and_default_message()
    {
        var result = CodexLoginResult.Success();

        Assert.Equal(CodexLoginResultCategory.Success, result.Category);
        Assert.Equal("Codex login successful", result.Message);
    }

    [Fact]
    public void Timeout_has_timeout_category_and_default_message()
    {
        var result = CodexLoginResult.Timeout();

        Assert.Equal(CodexLoginResultCategory.Timeout, result.Category);
        Assert.Equal("Codex login timed out", result.Message);
    }

    [Fact]
    public void Cancelled_has_cancelled_category_and_default_message()
    {
        var result = CodexLoginResult.Cancelled();

        Assert.Equal(CodexLoginResultCategory.Cancelled, result.Category);
        Assert.Equal("Codex login cancelled", result.Message);
    }

    [Fact]
    public void ManualInterventionRequired_preserves_reason()
    {
        var result = CodexLoginResult.ManualInterventionRequired("Complete browser verification");

        Assert.Equal(CodexLoginResultCategory.ManualInterventionRequired, result.Category);
        Assert.Equal("Complete browser verification", result.Message);
    }

    [Fact]
    public void Failed_preserves_reason()
    {
        var result = CodexLoginResult.Failed("Provider rejected login");

        Assert.Equal(CodexLoginResultCategory.Failed, result.Category);
        Assert.Equal("Provider rejected login", result.Message);
    }
}
