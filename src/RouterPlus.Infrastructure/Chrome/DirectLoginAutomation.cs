using System.Text.Json;
using RouterPlus.Core.Observability;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Base class for direct provider login automation (email/password/TOTP).
/// Subclasses implement provider-specific selectors and steps.
/// </summary>
public abstract class DirectLoginAutomation
{
    protected readonly ChromeCdpClient _client;
    protected readonly string _sessionId;
    protected readonly string _targetId;
    protected readonly string _email;
    protected readonly string _password;
    protected readonly Func<Task<string?>>? _totpGenerator;

    protected DirectLoginAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string email,
        string password,
        Func<Task<string?>>? totpGenerator = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _targetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _password = password ?? throw new ArgumentNullException(nameof(password));
        _totpGenerator = totpGenerator;
    }

    /// <summary>
    /// Run the full direct login flow.
    /// </summary>
    public async Task<DirectLoginResult> RunAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var totpAttempted = false;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "DirectLogin",
            "RunAsyncStarted",
            "Direct login automation loop started",
            new { timeout_seconds = timeout.TotalSeconds });

        var clickedLoginButton = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // If no email field and haven't clicked login button yet, try to click it
            if (!clickedLoginButton && !await IsElementVisibleAsync(GetEmailSelector(), cancellationToken))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "DirectLogin",
                    "TryClickLoginButton",
                    "Email field not visible, attempting to click Log in button");

                if (await TryClickLoginButtonAsync(cancellationToken))
                {
                    clickedLoginButton = true;
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "DirectLogin",
                        "LoginButtonClicked",
                        "Clicked Log in button, waiting for email field");
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }
            }

            // Wait for email field
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "DirectLogin",
                "WaitingForEmailField",
                "Waiting for email input field to appear");

            if (!await WaitForSelectorAsync(GetEmailSelector(), cancellationToken))
            {
                // Log current URL when email field not found
                var currentUrl = await GetCurrentUrlAsync(cancellationToken);
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "DirectLogin",
                    "EmailFieldNotFound",
                    "Email field not found, retrying",
                    new { current_url = currentUrl });

                await Task.Delay(500, cancellationToken);
                continue;
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "DirectLogin",
                "EmailFieldFound",
                "Email field found, filling credentials");

            // Fill email
            await FillEmailAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "DirectLogin",
                "EmailFilled",
                "Email field filled");

            // Fill password
            await FillPasswordAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "DirectLogin",
                "PasswordFilled",
                "Password field filled");

            // Submit
            await SubmitLoginAsync(cancellationToken);
            await Task.Delay(2000, cancellationToken);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "DirectLogin",
                "LoginSubmitted",
                "Login form submitted, waiting for response");

            // Check if TOTP is required
            if (await IsTotpRequiredAsync(cancellationToken))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "DirectLogin",
                    "TotpRequired",
                    "TOTP challenge detected");

                if (!totpAttempted && _totpGenerator is not null)
                {
                    totpAttempted = true;
                    var totpCode = await _totpGenerator();
                    if (!string.IsNullOrWhiteSpace(totpCode))
                    {
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Debug,
                            "DirectLogin",
                            "TotpAutoFilling",
                            "Auto-filling TOTP code for direct login");
                        await FillTotpAsync(totpCode, cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                        await SubmitTotpAsync(cancellationToken);
                        await Task.Delay(2000, cancellationToken);
                    }
                }
            }

            // Wait for completion
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "DirectLogin",
                "CheckingCompletion",
                "Checking if login completed");

            if (await IsLoginCompleteAsync(cancellationToken))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "DirectLogin",
                    "LoginCompleted",
                    "Login completion detected");
                return new DirectLoginResult(Success: true, Message: "Login completed");
            }
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Warning,
            "DirectLogin",
            "TimeoutReached",
            "Direct login automation timed out",
            new { timeout_seconds = timeout.TotalSeconds });

        return new DirectLoginResult(Success: false, Message: "Timeout waiting for login completion");
    }

    // ========== Abstract methods (provider-specific) ==========

    /// <summary>
    /// Get CSS selector for the email/username input field.
    /// </summary>
    protected abstract string GetEmailSelector();

    /// <summary>
    /// Get CSS selector for the password input field.
    /// </summary>
    protected abstract string GetPasswordSelector();

    /// <summary>
    /// Get CSS selector for the TOTP input field (if applicable).
    /// </summary>
    protected abstract string? GetTotpSelector();

    /// <summary>
    /// Get CSS selector for the login submit button.
    /// </summary>
    protected abstract string GetSubmitSelector();

    /// <summary>
    /// Get URL or condition that indicates login completed.
    /// </summary>
    protected abstract Task<bool> IsLoginCompleteAsync(CancellationToken cancellationToken);

    // ========== Template method hooks ==========

    /// <summary>
    /// Fill email input. Default uses FillInputAsync with email selector.
    /// </summary>
    protected virtual async Task FillEmailAsync(CancellationToken cancellationToken)
    {
        await FillInputAsync(GetEmailSelector(), _email, cancellationToken);
    }

    /// <summary>
    /// Fill password input. Default uses FillInputAsync with password selector.
    /// </summary>
    protected virtual async Task FillPasswordAsync(CancellationToken cancellationToken)
    {
        await FillInputAsync(GetPasswordSelector(), _password, cancellationToken);
    }

    /// <summary>
    /// Fill TOTP input.
    /// </summary>
    protected virtual async Task FillTotpAsync(string totpCode, CancellationToken cancellationToken)
    {
        var selector = GetTotpSelector();
        if (string.IsNullOrEmpty(selector))
        {
            throw new InvalidOperationException("TOTP selector not defined for this provider");
        }
        await FillInputAsync(selector, totpCode, cancellationToken);
    }

    /// <summary>
    /// Submit login form. Default clicks submit button.
    /// </summary>
    protected virtual async Task SubmitLoginAsync(CancellationToken cancellationToken)
    {
        await ClickAsync(GetSubmitSelector(), cancellationToken);
    }

    /// <summary>
    /// Submit TOTP form. Default clicks submit button.
    /// </summary>
    protected virtual async Task SubmitTotpAsync(CancellationToken cancellationToken)
    {
        await ClickAsync(GetSubmitSelector(), cancellationToken);
    }

    /// <summary>
    /// Check if TOTP input is visible.
    /// </summary>
    protected virtual async Task<bool> IsTotpRequiredAsync(CancellationToken cancellationToken)
    {
        var selector = GetTotpSelector();
        if (string.IsNullOrEmpty(selector))
            return false;

        return await IsElementVisibleAsync(selector, cancellationToken);
    }

    // ========== Shared CDP helpers ==========

    protected async Task<bool> WaitForSelectorAsync(string selector, CancellationToken cancellationToken, int timeoutMs = 5000)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsElementVisibleAsync(selector, cancellationToken))
                return true;
            await Task.Delay(200, cancellationToken);
        }
        return false;
    }

    protected async Task<bool> IsElementVisibleAsync(string selector, CancellationToken cancellationToken)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var script = $@"
(function() {{
    const elements = document.querySelectorAll({selectorJson});
    return Array.from(elements).some(el => {{
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    }});
}})()
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

    protected async Task FillInputAsync(string selector, string value, CancellationToken cancellationToken)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var valueJson = JsonSerializer.Serialize(value);
        var script = $@"
(function() {{
    const elements = document.querySelectorAll({selectorJson});
    const element = Array.from(elements).find(el => {{
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    }});
    if (!element) throw new Error('Element not found: ' + {selectorJson});
    element.focus();
    element.value = {valueJson};
    element.dispatchEvent(new Event('input', {{ bubbles: true }}));
    element.dispatchEvent(new Event('change', {{ bubbles: true }}));
    return true;
}})()
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true
        }, cancellationToken, _sessionId);

        if (result.TryGetProperty("exceptionDetails", out _))
        {
            throw new InvalidOperationException($"Failed to fill input with selector: {selector}");
        }
    }

    protected async Task ClickAsync(string selector, CancellationToken cancellationToken)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var script = $@"
(function() {{
    const elements = document.querySelectorAll({selectorJson});
    const element = Array.from(elements).find(el => {{
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    }});
    if (!element) throw new Error('Element not found: ' + {selectorJson});
    element.click();
    return true;
}})()
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true
        }, cancellationToken, _sessionId);

        if (result.TryGetProperty("exceptionDetails", out _))
        {
            throw new InvalidOperationException($"Failed to click element with selector: {selector}");
        }
    }

    protected async Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = "window.location.href",
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var valueProp))
            {
                return valueProp.GetString() ?? "unknown";
            }
            return "unknown";
        }
        catch
        {
            return "error";
        }
    }

    protected virtual async Task<bool> TryClickLoginButtonAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);
    const loginButton = buttons.find(btn => {
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('log in') || text.includes('sign in') || text.includes('đăng nhập');
    });
    if (loginButton) {
        loginButton.click();
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

/// <summary>
/// Result of direct login flow.
/// </summary>
public sealed record DirectLoginResult(
    bool Success,
    string Message);
