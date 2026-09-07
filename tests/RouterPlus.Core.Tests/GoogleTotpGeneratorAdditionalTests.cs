using RouterPlus.Core.Security;

namespace RouterPlus.Core.Tests;

public sealed class GoogleTotpGeneratorAdditionalTests
{
    [Fact]
    public void Generate_matches_rfc6238_eight_digit_vector()
    {
        // Arrange
        var secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var utcTime = DateTimeOffset.FromUnixTimeSeconds(59);

        // Act
        var code = GoogleTotpGenerator.Generate(secret, utcTime, digits: 8, periodSeconds: 30);

        // Assert
        Assert.Equal("94287082", code);
    }

}
