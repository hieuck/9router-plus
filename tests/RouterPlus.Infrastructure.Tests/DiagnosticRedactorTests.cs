using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// Behavior tests for <see cref="DiagnosticRedactor"/>.
///
/// Task 1.3 requirement: prove synthetic password, TOTP, API key, bearer
/// token, cookie, query value, hash value and email value are absent from
/// logs/artifacts, while allowlisted structural content (booleans, counts,
/// fixed category names, host names, path segments) is kept.
/// </summary>
public sealed class DiagnosticRedactorTests
{
    // === Synthetic secret markers ===

    private const string SyntheticPassword = "S3cret!Passw0rd";
    private const string SyntheticTotp = "471293";
    private const string SyntheticApiKey = "AIzaSyD-synthetic-api-key-9x";
    private const string SyntheticBearer = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
    private const string SyntheticCookie = "sess-abc-4711";
    private const string SyntheticQueryValue = "OAuth2-AuthorizationCode";
    private const string SyntheticHashValue = "ya29-synthetic-token";
    private const string SyntheticEmail = "jane.doe+google@accounts.example.test";

    // === Core contract ===

    [Fact]
    public void Redact_null_returns_empty_string()
    {
        Assert.Equal(string.Empty, DiagnosticRedactor.Redact(null));
        Assert.Equal(string.Empty, DiagnosticRedactor.Redact(""));
    }

    [Fact]
    public void Redact_is_deterministic()
    {
        const string input = "user=" + SyntheticEmail + " totp=" + SyntheticTotp;

        var first = DiagnosticRedactor.Redact(input);
        var second = DiagnosticRedactor.Redact(input);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Redact_is_idempotent()
    {
        const string input = "https://accounts.google.com/signin?code=" + SyntheticQueryValue +
                             "#access_token=" + SyntheticHashValue;

        var once = DiagnosticRedactor.Redact(input);
        var twice = DiagnosticRedactor.Redact(once);

        Assert.Equal(once, twice);
    }

    // === Allowlist: structural content is preserved ===

    [Fact]
    public void Redact_preserves_allowlisted_structural_content()
    {
        const string input = "host=accounts.google.com path=/signin/v3/identifier " +
                             "hasEmailField=true hasPasswordField=false hasTotpField=true " +
                             "alerts=2 forms=1 visibleButtons=3 searchParamCount=0 hasHash=false " +
                             "verifyYou=false chooseAccount=true loading=true";

        var redacted = DiagnosticRedactor.Redact(input);

        Assert.Equal(input, redacted);
    }

    [Fact]
    public void Redact_preserves_vietnamese_fixed_category_markers()
    {
        // The diagnostics use Vietnamese fixed category markers. Redaction must not mangle them.
        const string input = "đăng nhập bỏ qua mã xác minh xác thực";

        var redacted = DiagnosticRedactor.Redact(input);

        Assert.Equal(input, redacted);
    }

    [Fact]
    public void Redact_preserves_json_allowlist_shape()
    {
        // The allowlisted probe JSON emitted by CaptureTransitionDiagnosticAsync must pass through.
        const string input = "{\"host\":\"accounts.google.com\",\"path\":\"/signin/v3/identifier\"," +
                             "\"searchParamCount\":0,\"hasHash\":false,\"hasEmailField\":true," +
                             "\"hasPasswordField\":false,\"hasTotpField\":true,\"buttonCandidates\":2," +
                             "\"alerts\":1,\"forms\":1,\"hasAccountPicker\":false," +
                             "\"textMarkers\":{\"chooseAccount\":false,\"signInText\":true," +
                             "\"loading\":true,\"visibleButtons\":3}}";

        var redacted = DiagnosticRedactor.Redact(input);

        Assert.Equal(input, redacted);
    }

    // === Redaction: each synthetic secret class is absent from the result ===

    [Theory]
    [InlineData("password=" + SyntheticPassword)]
    [InlineData("pwd=" + SyntheticPassword)]
    [InlineData("passwd=" + SyntheticPassword)]
    [InlineData("\"password\":\"" + SyntheticPassword + "\"")]
    [InlineData("password: " + SyntheticPassword)]
    public void Redact_removes_synthetic_password(string input)
    {
        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticPassword, redacted);
    }

    [Theory]
    [InlineData("totp=" + SyntheticTotp)]
    [InlineData("otp=" + SyntheticTotp)]
    [InlineData("\"totp\":\"" + SyntheticTotp + "\"")]
    public void Redact_removes_synthetic_totp(string input)
    {
        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticTotp, redacted);
    }

    [Theory]
    [InlineData("api_key=" + SyntheticApiKey)]
    [InlineData("apikey=" + SyntheticApiKey)]
    [InlineData("key=" + SyntheticApiKey)]
    [InlineData("client_secret=" + SyntheticApiKey)]
    [InlineData("access_token=" + SyntheticApiKey)]
    public void Redact_removes_synthetic_api_key(string input)
    {
        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticApiKey, redacted);
    }

    [Theory]
    [InlineData("Authorization: Bearer " + SyntheticBearer)]
    [InlineData("Bearer " + SyntheticBearer)]
    [InlineData("token=" + SyntheticBearer)]
    [InlineData("authorization=Bearer " + SyntheticBearer)]
    public void Redact_removes_synthetic_bearer_token(string input)
    {
        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticBearer, redacted);
    }

    [Theory]
    [InlineData("Cookie: session=" + SyntheticCookie)]
    [InlineData("cookie=" + SyntheticCookie)]
    [InlineData("Cookie: theme=dark; session=" + SyntheticCookie)]
    public void Redact_removes_synthetic_cookie(string input)
    {
        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticCookie, redacted);
    }

    [Fact]
    public void Redact_removes_synthetic_query_values()
    {
        const string input = "https://accounts.google.com/o/oauth2/v2/auth?client_id=abc.apps.googleusercontent.com" +
                            "&scope=openid+email&code=" + SyntheticQueryValue +
                            "&redirect_uri=https%3A//app.example.test/cb";

        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticQueryValue, redacted);
        Assert.DoesNotContain("abc.apps.googleusercontent.com", redacted);
        Assert.DoesNotContain("openid+email", redacted);
        // Host and path are allowlisted.
        Assert.Contains("accounts.google.com", redacted);
        Assert.Contains("/o/oauth2/v2/auth", redacted);
    }

    [Fact]
    public void Redact_removes_synthetic_hash_values()
    {
        const string input = "https://accounts.google.com/signin#access_token=" + SyntheticHashValue;

        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticHashValue, redacted);
        Assert.Contains("accounts.google.com", redacted);
        Assert.Contains("/signin", redacted);
    }

    [Theory]
    [InlineData(SyntheticEmail)]
    [InlineData("user=" + SyntheticEmail)]
    [InlineData("LinkedGoogleAccount: " + SyntheticEmail)]
    public void Redact_removes_synthetic_email_value(string input)
    {
        var redacted = DiagnosticRedactor.Redact(input);

        Assert.DoesNotContain(SyntheticEmail, redacted);
    }

    // === Absence proof over probe-shaped and artifact-shaped content ===

    [Fact]
    public void Redact_probe_shaped_dump_contains_no_synthetic_secret_markers()
    {
        // Simulates the JSON dump a page probe could previously produce, with
        // synthetic secrets smuggled into search/hash/iframes/inputs/samples.
        const string rawDump =
            "{\"host\":\"accounts.google.com\",\"path\":\"/signin/v3/identifier\"," +
            "\"search\":\"?code=" + SyntheticQueryValue + "&continue=https%3A%2F%2Fapp.example.test%2Fcb\"," +
            "\"hash\":\"#access_token=" + SyntheticHashValue + "\"," +
            "\"iframes\":\"login|https://accounts.google.com/embedded?key=" + SyntheticApiKey + "\"," +
            "\"inputs\":\"email:identifier:username;password::current-password\"," +
            "\"visibleClickableSample\":\"button:Continue:aria\",\"emailValue\":\"" + SyntheticEmail + "\"," +
            "\"passwordValue\":\"" + SyntheticPassword + "\",\"totpValue\":\"" + SyntheticTotp + "\"," +
            "\"cookie\":\"" + SyntheticCookie + "\"}";

        var redacted = DiagnosticRedactor.Redact(rawDump);

        Assert.DoesNotContain(SyntheticQueryValue, redacted);
        Assert.DoesNotContain(SyntheticHashValue, redacted);
        Assert.DoesNotContain(SyntheticApiKey, redacted);
        Assert.DoesNotContain(SyntheticEmail, redacted);
        Assert.DoesNotContain(SyntheticPassword, redacted);
        Assert.DoesNotContain(SyntheticTotp, redacted);
        Assert.DoesNotContain(SyntheticCookie, redacted);
    }

    [Fact]
    public void Redact_diagnostic_file_line_contains_no_synthetic_secret_markers()
    {
        // Captures the replace path in CaptureTransitionDiagnosticAsync: the redactor is
        // applied to the file line (timestamp + field + JSON) before it is appended.
        const string line =
            "[2026-08-30T10:00:00.0000000+00:00] field=Email " +
            "{\"host\":\"accounts.google.com\",\"path\":\"/signin/v3/identifier\"," +
            "\"search\":\"?code=" + SyntheticQueryValue + "\"," +
            "\"hash\":\"#" + SyntheticHashValue + "\"," +
            "\"emailValue\":\"" + SyntheticEmail + "\"}";

        var redacted = DiagnosticRedactor.Redact(line);

        Assert.DoesNotContain(SyntheticQueryValue, redacted);
        Assert.DoesNotContain(SyntheticHashValue, redacted);
        Assert.DoesNotContain(SyntheticEmail, redacted);
        // Allowlisted shape survives.
        Assert.Contains("field=Email", redacted);
        Assert.Contains("accounts.google.com", redacted);
        Assert.Contains("/signin/v3/identifier", redacted);
    }
}