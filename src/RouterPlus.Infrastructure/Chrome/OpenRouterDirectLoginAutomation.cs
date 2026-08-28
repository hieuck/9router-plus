namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Direct login automation for OpenRouter.
/// Uses email + password + optional TOTP.
/// </summary>
public sealed class OpenRouterDirectLoginAutomation : DirectLoginAutomation
{
    public OpenRouterDirectLoginAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string email,
        string password,
        Func<Task<string?>>? totpGenerator = null)
        : base(client, sessionId, targetId, email, password, totpGenerator)
    {
    }

    protected override string GetEmailSelector() => "input[type='email'], input[name='email'], input#email";

    protected override string GetPasswordSelector() => "input[type='password'], input[name='password'], input#password";

    protected override string? GetTotpSelector() => "input[name='otp'], input[name='totp'], input[name='2fa-code'], input#otp";

    protected override string GetSubmitSelector() => "button[type='submit'], input[type='submit']";

    protected override async Task<bool> IsLoginCompleteAsync(CancellationToken cancellationToken)
    {
        // OpenRouter redirects to openrouter.ai dashboard after login
        var script = @"
(function() {
    const host = window.location.host;
    const path = window.location.pathname;

    // On dashboard or any non-login page = success
    if (host === 'openrouter.ai' && !path.includes('/login') && !path.includes('/signin') && !path.includes('/auth/')) {
        const emailField = document.querySelector('input[type=""email""]');
        if (!emailField) return true;
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
