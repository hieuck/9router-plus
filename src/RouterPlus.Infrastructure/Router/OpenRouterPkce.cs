using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Router;

public sealed record OpenRouterPkcePair(string Verifier, string Challenge);

public sealed record OpenRouterPkceResult(bool Success, string? ApiKey, string? ErrorMessage)
{
    public static OpenRouterPkceResult Succeeded(string apiKey) => new(true, apiKey, null);

    public static OpenRouterPkceResult Failed(string errorMessage) => new(false, null, errorMessage);
}

public static class OpenRouterPkce
{
    public const string AuthorizationEndpoint = "https://openrouter.ai/auth";
    public const string KeyExchangeEndpoint = "https://openrouter.ai/api/v1/auth/keys";
    public const string ChallengeMethod = "S256";

    public static OpenRouterPkcePair CreateS256Pair()
    {
        var verifier = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new OpenRouterPkcePair(verifier, challenge);
    }

    public static Uri BuildAuthorizationUrl(Uri callbackUrl, string codeChallenge)
    {
        ArgumentNullException.ThrowIfNull(callbackUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        var query =
            $"callback_url={Uri.EscapeDataString(callbackUrl.AbsoluteUri)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method={ChallengeMethod}";
        return new Uri($"{AuthorizationEndpoint}?{query}");
    }

    public static bool TryGetAuthorizationCode(
        OAuthCallbackData callback,
        out string? code,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            code = null;
            errorMessage = string.IsNullOrWhiteSpace(callback.ErrorDescription)
                ? callback.Error
                : callback.ErrorDescription;
            return false;
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            code = null;
            errorMessage = "OpenRouter did not return an authorization code.";
            return false;
        }

        code = callback.Code;
        errorMessage = null;
        return true;
    }

    public static async Task<OpenRouterPkceResult> ExchangeCodeForApiKeyAsync(
        HttpClient httpClient,
        string code,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        using var response = await httpClient.PostAsJsonAsync(
            KeyExchangeEndpoint,
            new OpenRouterKeyExchangeRequest(code, codeVerifier, ChallengeMethod),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return OpenRouterPkceResult.Failed(MapExchangeError((int)response.StatusCode, body));
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        if (!document.RootElement.TryGetProperty("key", out var keyElement))
        {
            return OpenRouterPkceResult.Failed("OpenRouter did not return an API key.");
        }

        var key = keyElement.GetString();
        return string.IsNullOrWhiteSpace(key)
            ? OpenRouterPkceResult.Failed("OpenRouter did not return an API key.")
            : OpenRouterPkceResult.Succeeded(key);
    }

    internal static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string MapExchangeError(int statusCode, string body)
    {
        if (statusCode == 403 && body.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return "Authorization code expired. Start the OpenRouter OAuth flow again.";
        }

        if (statusCode == 403)
        {
            return "Invalid OpenRouter authorization code. Start the OAuth flow again.";
        }

        if (statusCode == 400)
        {
            return "Invalid OpenRouter PKCE challenge method.";
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"OpenRouter key exchange failed ({statusCode})."
            : $"OpenRouter key exchange failed ({statusCode}): {body}";
    }

    private sealed record OpenRouterKeyExchangeRequest(
        string code,
        string code_verifier,
        string code_challenge_method);
}
