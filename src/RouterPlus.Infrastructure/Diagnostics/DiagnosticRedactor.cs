using System.Text.RegularExpressions;

namespace RouterPlus.Infrastructure.Diagnostics;

/// <summary>
/// Deterministic, dependency-free redaction of diagnostic strings.
///
/// Security invariant (see plan Task 1.3): never write password, TOTP, API
/// key, token, cookie, or authorization header into logs/artifacts. Applied
/// to diagnostic line content so the output artifact contains NO synthetic
/// secret markers (password, TOTP, API key, bearer token, cookie, query
/// value, hash value, email value).
///
/// Allowlist semantics: the redactor preserves structural content (boolean
/// flags, counts, fixed category names, JSON punctuation, host names and
/// path segments) and substitutes secret-bearing values with a fixed
/// placeholder <redacted>. It is safe to call on any string; null/empty
/// inputs are returned unchanged.
/// </summary>
public static partial class DiagnosticRedactor
{
    private const string Redacted = "<redacted>";

    /// <summary>
    /// Returns a safe diagnostic string with secret-bearing content replaced
    /// by <c>&lt;redacted&gt;</c>. Deterministic and free of any browser or
    /// I/O dependency.
    /// </summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var value = input;

        // Mask JSON string values whose property name marks them as a secret,
        // e.g. {"password":"...","totp":"...","access_token":"...","emailValue":"..."}.
        // Quoted-string values are anchored so structural tokens like the
        // bare marker name "password" are left alone.
        value = JsonSecretPropertyValues().Replace(value, "$1:\"<redacted>\"");

        // Mask query parameter values for secret-bearing keys.
        value = QuerySecretKeyPattern().Replace(value, "<redacted>");

        // Bearer / Basic authorization headers.
        value = AuthorizationHeaderPattern().Replace(value, "$1$2$3<redacted>");

        // Standalone "Bearer <token>" (not preceded by an authorization key),
        // e.g. a bare token echoed back by page content.
        value = StandaloneBearerTokenPattern().Replace(value, "<redacted>");

        // Whole-key or whole-value secrets.
        value = StandaloneSecretKeyPattern().Replace(value, "$1<redacted>");
        value = StandaloneEmailValuePattern().Replace(value, "$1<redacted>");

        // Email addresses (anywhere, including inside host-like substrings).
        value = EmailAddressPattern().Replace(value, "<redacted>");

        // URL query strings that survive the query-key pass (e.g. an encoded
        // code value spanning segments) — every value becomes <redacted>.
        value = UrlQueryPattern().Replace(value, "<redacted>");

        // URL hash fragments that carry a value (and are not just a page anchor).
        value = UrlHashPattern().Replace(value, "<redacted>");

        // JWT or API-key shaped long tokens.
        value = JwtShapePattern().Replace(value, "<redacted>");
        value = LongTokenShapePattern().Replace(value, "<redacted>");

        return value;
    }

    [GeneratedRegex(
        @"(""(?:password|passwd|pwd|totp|otp|api[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token|authorization|token|cookie|session|email|emailValue|hash|code|search|passwordValue|totpValue|client_id|redirect_uri|state|nonce|id_token|sessionState)"")" +
        @"\s*:\s*""(?:[^""\\]|\\.)*""",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecretPropertyValues();

    [GeneratedRegex(@"(?<=[?&](?:code|token|auth|key|api_key|apikey|client_secret|session|password|pwd|totp|otp|email|cookie)=)[^&\s""']*",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecretKeyPattern();

    [GeneratedRegex(@"\b(authorization|auth|cookie)\b(\s*[=:])(\s*(?:Bearer\s+)?)([A-Za-z0-9._~+/=-]{6,})",
        RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"(?<![\w])Bearer\s+[A-Za-z0-9._~+/=-]{8,}(?![\w])",
        RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneBearerTokenPattern();

    [GeneratedRegex(
        @"(?<![\w])(password|passwd|pwd|totp|otp|api[_-]?key|key|client[_-]?secret|access[_-]?token|refresh[_-]?token|cookie|session)" +
        @"(?![\w])\s*[=:]\s*[^&\s""']+",
        RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneSecretKeyPattern();

    [GeneratedRegex(@"\b(email)\b\s*[=:]\s*[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
        RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneEmailValuePattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressPattern();

    [GeneratedRegex(@"(?<=https?://[^?#\s]*\?)[^#\s]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex UrlQueryPattern();

    [GeneratedRegex(@"#[^\s""']+",
        RegexOptions.CultureInvariant)]
    private static partial Regex UrlHashPattern();

    [GeneratedRegex(@"(?<![\w])[A-Za-z0-9_]{28,}(?![\w])")]
    private static partial Regex LongTokenShapePattern();

    [GeneratedRegex(@"\b[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{20,}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex JwtShapePattern();
}