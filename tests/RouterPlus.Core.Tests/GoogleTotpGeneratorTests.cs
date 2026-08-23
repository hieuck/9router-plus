using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleTotpGeneratorTests
{
    [Fact]
    public void Generate_produces_standard_rfc6238_test_vector()
    {
        // RFC 6238 standard test vector at Unix time 59
        // Secret: "12345678901234567890" (20 bytes)
        // Base32: GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var utcTime = DateTimeOffset.FromUnixTimeSeconds(59);

        var code = GoogleTotpGenerator.Generate(secret, utcTime, digits: 6, periodSeconds: 30);

        Assert.Equal("287082", code);
    }

    [Fact]
    public void Generate_handles_spaces_and_hyphens_in_secret()
    {
        var secretWithSpaces = "GEZDGNBV GY3TQOJQ GEZDGNBV GY3TQOJQ";
        var secretWithHyphens = "GEZDGNBV-GY3TQOJQ-GEZDGNBV-GY3TQOJQ";
        var secretClean = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var utcTime = DateTimeOffset.FromUnixTimeSeconds(59);

        var code1 = GoogleTotpGenerator.Generate(secretWithSpaces, utcTime);
        var code2 = GoogleTotpGenerator.Generate(secretWithHyphens, utcTime);
        var code3 = GoogleTotpGenerator.Generate(secretClean, utcTime);

        Assert.Equal(code3, code1);
        Assert.Equal(code3, code2);
    }

    [Fact]
    public void Generate_rejects_invalid_base32_characters()
    {
        var invalidSecret = "INVALID1@#$";
        var utcTime = DateTimeOffset.UtcNow;

        Assert.Throws<FormatException>(() => GoogleTotpGenerator.Generate(invalidSecret, utcTime));
    }

    [Fact]
    public void Generate_rejects_empty_or_whitespace_secret()
    {
        var utcTime = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => GoogleTotpGenerator.Generate("", utcTime));
        Assert.Throws<ArgumentException>(() => GoogleTotpGenerator.Generate("   ", utcTime));
    }

    [Fact]
    public void Generate_rejects_non_positive_digits()
    {
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var utcTime = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleTotpGenerator.Generate(secret, utcTime, digits: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleTotpGenerator.Generate(secret, utcTime, digits: -1));
    }

    [Fact]
    public void Generate_rejects_non_positive_period()
    {
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var utcTime = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleTotpGenerator.Generate(secret, utcTime, periodSeconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleTotpGenerator.Generate(secret, utcTime, periodSeconds: -1));
    }

    [Fact]
    public void Generate_pads_output_to_requested_digits()
    {
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var utcTime = DateTimeOffset.FromUnixTimeSeconds(1);

        var code6 = GoogleTotpGenerator.Generate(secret, utcTime, digits: 6);
        var code8 = GoogleTotpGenerator.Generate(secret, utcTime, digits: 8);

        Assert.Equal(6, code6.Length);
        Assert.Equal(8, code8.Length);
        Assert.All(code6, c => Assert.True(char.IsDigit(c)));
        Assert.All(code8, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void Generate_produces_different_codes_for_different_time_windows()
    {
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var time1 = DateTimeOffset.FromUnixTimeSeconds(30);
        var time2 = DateTimeOffset.FromUnixTimeSeconds(60);

        var code1 = GoogleTotpGenerator.Generate(secret, time1);
        var code2 = GoogleTotpGenerator.Generate(secret, time2);

        Assert.NotEqual(code1, code2);
    }

    [Fact]
    public void Generate_produces_same_code_within_same_time_window()
    {
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var time1 = DateTimeOffset.FromUnixTimeSeconds(45);
        var time2 = DateTimeOffset.FromUnixTimeSeconds(59);

        var code1 = GoogleTotpGenerator.Generate(secret, time1, periodSeconds: 30);
        var code2 = GoogleTotpGenerator.Generate(secret, time2, periodSeconds: 30);

        Assert.Equal(code1, code2);
    }

    [Fact]
    public void Generate_is_case_insensitive_for_base32()
    {
        var secretUpper = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var secretLower = "gezdgnbvgy3tqojqgezdgnbvgy3tqojq";
        var secretMixed = "GezDGnBvGy3TQoJqGeZdGnBvGy3TqOjQ";
        var utcTime = DateTimeOffset.FromUnixTimeSeconds(59);

        var code1 = GoogleTotpGenerator.Generate(secretUpper, utcTime);
        var code2 = GoogleTotpGenerator.Generate(secretLower, utcTime);
        var code3 = GoogleTotpGenerator.Generate(secretMixed, utcTime);

        Assert.Equal(code1, code2);
        Assert.Equal(code1, code3);
    }
}
