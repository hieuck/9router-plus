using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Direct login automation for GitHub.
/// Uses email/username + password + optional TOTP.
/// </summary>
public sealed class GitHubDirectLoginAutomation : DirectLoginAutomation
{
    public GitHubDirectLoginAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string email,
        string password,
        Func<Task<string?>>? totpGenerator = null)
        : base(client, sessionId, targetId, email, password, totpGenerator)
    {
    }

    protected override string GetEmailSelector() => "input[name='login'], input#login_field, input[name='email']";

    protected override string GetPasswordSelector() => "input[name='password'], input#password";

    protected override string? GetTotpSelector() => "input[name='otp'], input#otp, input[name='app_otp']";

    protected override string GetSubmitSelector() => "input[type='submit'][name='commit'], button[type='submit']";

    protected override async Task<bool> IsLoginCompleteAsync(CancellationToken cancellationToken)
    {
        // GitHub redirects to github.com dashboard after login
        // Also check for 2FA page (otp input visible)
        var script = @"
(function() {
    const host = window.location.host;
    const path = window.location.pathname;
    const url = window.location.href;

    // On dashboard or any non-login page = success
    if (host === 'github.com' && !path.includes('/login') && !path.includes('/sessions/')) {
        // Check we're not still on login form
        const loginField = document.querySelector('input#login_field');
        if (!loginField) return true;
    }

    // Still on login page
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
