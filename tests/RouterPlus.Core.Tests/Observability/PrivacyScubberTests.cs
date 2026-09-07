using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class PrivacyScubberTests
{
    private enum SyntheticStatus
    {
        Ready
    }

    private sealed class ThrowingPropertySample
    {
        public string SafeValue => "kept";

        public string FailingValue => throw new InvalidOperationException("synthetic getter failure");
    }

    [Fact]
    public void Scrub_returns_null_for_null_input()
    {
        // Arrange
        object? input = null;

        // Act
        var scrubbed = PrivacyScrubber.Scrub(input);

        // Assert
        Assert.Null(scrubbed);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(true)]
    public void Scrub_preserves_primitive_values(object input)
    {
        // Arrange

        // Act
        var scrubbed = PrivacyScrubber.Scrub(input);

        // Assert
        Assert.Equal(input, scrubbed);
    }

    [Fact]
    public void Scrub_preserves_value_types_and_enums()
    {
        // Arrange
        var date = new DateTime(2026, 9, 7, 12, 30, 0, DateTimeKind.Utc);
        var status = SyntheticStatus.Ready;

        // Act
        var scrubbedDate = PrivacyScrubber.Scrub(date);
        var scrubbedStatus = PrivacyScrubber.Scrub(status);

        // Assert
        Assert.Equal(date, scrubbedDate);
        Assert.Equal(status, scrubbedStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ScrubString_preserves_null_or_empty_text(string? input)
    {
        // Arrange

        // Act
        var scrubbed = PrivacyScrubber.ScrubString(input!);

        // Assert
        Assert.Equal(input, scrubbed);
    }

    [Fact]
    public void Scrub_preserves_null_items_in_collections()
    {
        // Arrange
        var input = new object?[] { null, "password=synthetic-secret" };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(input) as List<object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Null(scrubbed[0]);
        Assert.Equal("password=[REDACTED]", scrubbed[1]);
    }

    [Fact]
    public void Scrub_marks_property_when_getter_throws()
    {
        // Arrange
        var input = new ThrowingPropertySample();

        // Act
        var scrubbed = PrivacyScrubber.Scrub(input) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Equal("kept", scrubbed["SafeValue"]);
        Assert.Equal("[ERROR_READING_PROPERTY]", scrubbed["FailingValue"]);
    }

    [Fact]
    public void Scrub_returns_null_for_null_input()
    {
        // Arrange
        object? value = null;

        // Act
        var scrubbed = PrivacyScrubber.Scrub(value);

        // Assert
        Assert.Null(scrubbed);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(true)]
    public void Scrub_preserves_primitive_values(object value)
    {
        // Act
        var scrubbed = PrivacyScrubber.Scrub(value);

        // Assert
        Assert.Equal(value, scrubbed);
    }

    [Fact]
    public void Scrub_preserves_value_types()
    {
        // Arrange
        var value = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);

        // Act
        var scrubbed = PrivacyScrubber.Scrub(value);

        // Assert
        Assert.Equal(value, scrubbed);
    }

    [Fact]
    public void Scrub_preserves_enum_values()
    {
        // Arrange
        var value = LogLevel.Warning;

        // Act
        var scrubbed = PrivacyScrubber.Scrub(value);

        // Assert
        Assert.Equal(value, scrubbed);
    }

    [Fact]
    public void Scrub_removes_password_property()
    {
        // Arrange
        var obj = new { Username = "user@example.com", Password = "secret123", ProfileName = "Test" };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Equal("user@example.com", scrubbed["Username"]);
        Assert.Equal("[REDACTED]", scrubbed["Password"]);
        Assert.Equal("Test", scrubbed["ProfileName"]);
    }

    [Fact]
    public void Scrub_removes_apikey_property()
    {
        // Arrange
        var obj = new { Provider = "OpenRouter", ApiKey = "sk_live_abc123", Email = "user@example.com" };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Equal("OpenRouter", scrubbed["Provider"]);
        Assert.Equal("[REDACTED]", scrubbed["ApiKey"]);
        Assert.Equal("user@example.com", scrubbed["Email"]);
    }

    [Fact]
    public void Scrub_removes_token_property()
    {
        // Arrange
        var obj = new { UserId = "123", AccessToken = "Bearer xyz", RefreshToken = "refresh_abc" };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Equal("123", scrubbed["UserId"]);
        Assert.Equal("[REDACTED]", scrubbed["AccessToken"]);
        Assert.Equal("[REDACTED]", scrubbed["RefreshToken"]);
    }

    [Fact]
    public void Scrub_removes_totpsecret_property()
    {
        // Arrange
        var obj = new { Email = "user@example.com", TotpSecret = "JBSWY3DPEHPK3PXP", ProfileId = "abc123" };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Equal("user@example.com", scrubbed["Email"]);
        Assert.Equal("[REDACTED]", scrubbed["TotpSecret"]);
        Assert.Equal("abc123", scrubbed["ProfileId"]);
    }

    [Fact]
    public void ScrubString_removes_password_patterns()
    {
        // Arrange
        var text = "Connection string: user=admin password=secret123 host=localhost";

        // Act
        var scrubbed = PrivacyScrubber.ScrubString(text);

        // Assert
        Assert.Contains("password=[REDACTED]", scrubbed);
        Assert.DoesNotContain("secret123", scrubbed);
    }

    [Fact]
    public void ScrubString_removes_apikey_patterns()
    {
        // Arrange
        var text = "Config: api_key=sk_live_abc123 endpoint=https://api.example.com";

        // Act
        var scrubbed = PrivacyScrubber.ScrubString(text);

        // Assert
        Assert.Contains("api_key=[REDACTED]", scrubbed);
        Assert.DoesNotContain("sk_live_abc123", scrubbed);
    }

    [Fact]
    public void Scrub_dictionary_preserves_entries_and_scrubs_sensitive_values()
    {
        // Arrange
        var obj = new Dictionary<string, object?>
        {
            ["Username"] = "user@example.com",
            ["Password"] = "secret123",
            ["Nested"] = new Dictionary<string, object?>
            {
                ["ApiKey"] = "key123"
            }
        };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        Assert.Equal("user@example.com", scrubbed["Username"]);
        Assert.Equal("[REDACTED]", scrubbed["Password"]);
        var nested = Assert.IsType<Dictionary<string, object?>>(scrubbed["Nested"]);
        Assert.Equal("[REDACTED]", nested["ApiKey"]);
    }

    [Fact]
    public void Scrub_preserves_non_string_scalar_values()
    {
        Assert.Equal(42, PrivacyScrubber.Scrub(42));
        Assert.Equal(true, PrivacyScrubber.Scrub(true));
        var timestamp = DateTime.UtcNow;
        Assert.Equal(timestamp, PrivacyScrubber.Scrub(timestamp));
    }

    [Fact]
    public void Scrub_scalar_string_still_scrubs_sensitive_patterns()
    {
        Assert.Equal("token=[REDACTED]", PrivacyScrubber.Scrub("token=secret123"));
    }

    [Fact]
    public void Scrub_handles_nested_objects()
    {
        // Arrange
        var obj = new
        {
            User = new { Name = "John", Password = "secret" },
            Settings = new { Theme = "Dark", ApiKey = "key123" }
        };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        var user = scrubbed["User"] as Dictionary<string, object?>;
        Assert.NotNull(user);
        Assert.Equal("John", user["Name"]);
        Assert.Equal("[REDACTED]", user["Password"]);

        var settings = scrubbed["Settings"] as Dictionary<string, object?>;
        Assert.NotNull(settings);
        Assert.Equal("Dark", settings["Theme"]);
        Assert.Equal("[REDACTED]", settings["ApiKey"]);
    }

    [Fact]
    public void Scrub_handles_collections()
    {
        // Arrange
        var obj = new
        {
            Users = new[]
            {
                new { Name = "Alice", Password = "pass1" },
                new { Name = "Bob", Password = "pass2" }
            }
        };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(scrubbed);
        var users = scrubbed["Users"] as List<object?>;
        Assert.NotNull(users);
        Assert.Equal(2, users.Count);

        var alice = users[0] as Dictionary<string, object?>;
        Assert.NotNull(alice);
        Assert.Equal("Alice", alice["Name"]);
        Assert.Equal("[REDACTED]", alice["Password"]);
    }

    [Fact]
    public void Scrub_preserves_allowed_properties()
    {
        // Arrange - Profile names and emails are allowed (user-visible)
        var obj = new
        {
            ProfileName = "Work Profile",
            Email = "user@example.com",
            ProfileId = "abc123",
            DirectoryName = "Default"
        };

        // Act
        var scrubbed = PrivacyScrubber.Scrub(obj) as Dictionary<string, object?>;

        // Assert - None of these should be redacted
        Assert.NotNull(scrubbed);
        Assert.Equal("Work Profile", scrubbed["ProfileName"]);
        Assert.Equal("user@example.com", scrubbed["Email"]);
        Assert.Equal("abc123", scrubbed["ProfileId"]);
        Assert.Equal("Default", scrubbed["DirectoryName"]);
    }
}
