using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Direct login automation for Kiro (AWS Builder ID).
/// Uses email + password + optional TOTP.
/// </summary>
public sealed class KiroDirectLoginAutomation : DirectLoginAutomation
{
    public KiroDirectLoginAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string email,
        string password,
        Func<Task<string?>>? totpGenerator = null)
        : base(client, sessionId, targetId, email, password, totpGenerator)
    {
    }

    protected override string GetEmailSelector() => "input[type='email'], input[name='email'], input#awsui-input-0";

    protected override string GetPasswordSelector() => "input[type='password'], input[name='password'], input#awsui-input-1";

    protected override string? GetTotpSelector() => "input[type='text'][name='mfacode'], input[name='otp'], input[placeholder*='verification']";

    protected override string GetSubmitSelector() => "button[type='submit'], input[type='submit'], button[class*='submit']";

    protected override async Task<bool> IsLoginCompleteAsync(CancellationToken cancellationToken)
    {
        // AWS Builder ID redirects to view.awsapps.com after login
        var script = @"
(function() {
    const host = window.location.host;
    const path = window.location.pathname;
    const url = window.location.href;

    // On AWS Builder ID dashboard/start page = success
    if (host === 'view.awsapps.com' && path.includes('/start')) {
        return true;
    }

    // On AWS SSO portal = success
    if (host.includes('.awsapps.com') && !path.includes('/login') && !path.includes('/auth')) {
        return true;
    }

    // Check if still on login pages
    if (host.includes('auth.us-east-1.amazoncognito.com') ||
        host.includes('profile.aws.amazon.com') ||
        (host.includes('.awsapps.com') && (path.includes('/login') || path.includes('/auth')))) {

        // Check if we're on password entry or MFA page (still logging in)
        const passwordField = document.querySelector('input[type=""password""]');
        const mfaField = document.querySelector('input[name=""mfacode""]');
        if (passwordField || mfaField) {
            return false;
        }
    }

    // If navigated to AWS dashboard area, consider success
    if (host.includes('awsapps.com') && path.includes('/start')) {
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
