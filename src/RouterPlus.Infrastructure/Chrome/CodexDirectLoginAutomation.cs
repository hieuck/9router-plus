using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Direct login automation for Codex (OpenAI/ChatGPT).
/// Uses email + password + optional TOTP.
/// </summary>
public sealed class CodexDirectLoginAutomation : DirectLoginAutomation
{
    public CodexDirectLoginAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string email,
        string password,
        Func<Task<string?>>? totpGenerator = null)
        : base(client, sessionId, targetId, email, password, totpGenerator)
    {
    }

    protected override string GetEmailSelector() => "input[type='email'], input[name='username'], input#username";

    protected override string GetPasswordSelector() => "input[type='password'], input[name='password'], input#password";

    protected override string? GetTotpSelector() => "input[type='text'][name='code'], input[name='otp'], input[autocomplete='one-time-code']";

    protected override string GetSubmitSelector() => "button[type='submit'], button[name='action'], input[type='submit']";

    protected override async Task<bool> IsLoginCompleteAsync(CancellationToken cancellationToken)
    {
        // OpenAI redirects to chatgpt.com or auth.openai.com callback after login
        var script = @"
(function() {
    const host = window.location.host;
    const path = window.location.pathname;
    const url = window.location.href;

    // On ChatGPT main page = success
    if (host === 'chatgpt.com' && !path.includes('/auth') && !path.includes('/login')) {
        return true;
    }

    // On OpenAI dashboard = success
    if (host === 'platform.openai.com' && !path.includes('/login')) {
        return true;
    }

    // Check if still on login/auth pages
    if (host === 'auth.openai.com' || host === 'chatgpt.com' && path.includes('/auth')) {
        // Check if we're on password entry or TOTP page (still logging in)
        const passwordField = document.querySelector('input[type=""password""]');
        const totpField = document.querySelector('input[autocomplete=""one-time-code""]');
        if (passwordField || totpField) {
            return false;
        }
    }

    // If navigated away from auth pages, consider success
    if (!host.includes('auth.openai.com') && !path.includes('/login')) {
        return true;
    }

    return false;
})()
";

        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var valueProp))
            {
                return valueProp.GetBoolean();
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
