using System.Text.Json;
using RouterPlus.Core.Security;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP adapter that implements IGoogleLoginBrowser for Google login automation.
/// </summary>
internal sealed class GoogleLoginCdpBrowser : IGoogleLoginBrowser
{
    private readonly ChromeCdpClient _client;
    private readonly string _sessionId;
    private readonly string _targetId;
    private bool _disposed;

    public GoogleLoginCdpBrowser(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client;
        _sessionId = sessionId;
        _targetId = targetId;
    }

    public async Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
    {
        using var renderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        renderCts.CancelAfter(TimeSpan.FromSeconds(30));
        var triedAddAccount = false;
        var triedSkipPasskey = false;
        var triedSkipHomeAddress = false;
        var emptyPageAttempts = 0;

        try
        {
            while (true)
            {
                var state = await ReadStateOnceAsync(renderCts.Token);
                if (state.HasEmailField || state.HasPasswordField || state.HasTotpField ||
                    state.HasCompletionSignal || state.HasManualChallenge)
                {
                    return state;
                }

                // Detect completely empty page (Google rendering bug) and force reload
                var isEmpty = !state.HasEmailField && !state.HasPasswordField && !state.HasTotpField
                    && !state.HasCompletionSignal && !state.HasManualChallenge && !state.Has2FAMethodPicker;
                if (isEmpty && state.PageUri.Host == "accounts.google.com" && emptyPageAttempts < 2)
                {
                    emptyPageAttempts++;
                    System.Console.WriteLine($"[ReadState] Empty page detected (attempt {emptyPageAttempts}), reloading...");
                    await _client.CallAsync("Page.reload", new { ignoreCache = true }, renderCts.Token);
                    await Task.Delay(2000, renderCts.Token);
                    continue;
                }

                // Google account chooser page renders no email field. Navigate
                // directly to the identifier entry page to bypass it.
                if (!triedAddAccount && state.PageUri.Host == "accounts.google.com")
                {
                    if (await TryBypassAccountChooserAsync(renderCts.Token))
                    {
                        triedAddAccount = true;
                    }
                }

                // Google passkey enrollment speedbump after successful authentication
                if (!triedSkipPasskey && state.PageUri.Host == "accounts.google.com" &&
                    (state.PageUri.AbsolutePath.Contains("/speedbump/passkey") ||
                     state.PageUri.AbsolutePath.Contains("/signin/speedbump") ||
                     state.PageUri.AbsolutePath.Contains("/verification/selfie/precollection")))
                {
                    if (await TrySkipPasskeyEnrollmentAsync(renderCts.Token))
                    {
                        triedSkipPasskey = true;
                        // Wait for navigation after skip
                        await Task.Delay(1000, renderCts.Token);
                    }
                }

                // Google home address collection speedbump
                if (!triedSkipHomeAddress && state.PageUri.Host == "accounts.google.com" &&
                    state.PageUri.AbsolutePath.Contains("/speedbump"))
                {
                    if (await TrySkipHomeAddressAsync(renderCts.Token))
                    {
                        triedSkipHomeAddress = true;
                        await Task.Delay(1000, renderCts.Token);
                    }
                }

                await Task.Delay(100, renderCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await CaptureTransitionDiagnosticAsync(GoogleLoginField.Email, cancellationToken);
            throw new TimeoutException("Google page did not render a controllable login state.");
        }
    }

    private async Task<GoogleLoginPageState> ReadStateOnceAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await ValidateTargetAsync(cancellationToken);

        const string script = @"
(function(args) {
    const pageUrl = window.location.href;
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const emailInput = document.querySelector('input[type=""email""], input[name=""identifier""], input[autocomplete*=""username"" i]');
    const hasEmailField = !!emailInput && (() => {
        const rect = emailInput.getBoundingClientRect();
        return emailInput.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    })();
    const emailValue = emailInput ? (emailInput.value || '') : '';
    const hasPasswordField = Array.from(document.querySelectorAll('input[type=""password""]')).some(isVisible);
    const hasTotpField = Array.from(document.querySelectorAll('input[name=""totpPin""]')).some(isVisible);
    const has2FAMethodPicker = !hasTotpField && (
        !!document.querySelector('[data-challengeindex], [data-challengetype], [data-challengeid]') ||
        window.location.pathname.includes('/challenge/selection') ||
        Array.from(document.querySelectorAll('li, div[role=""link""], div[role=""button""]')).some(el => {
            const text = (el.innerText || el.textContent || '').toLowerCase();
            return text.includes('authenticator') || text.includes('xác thực') ||
                   text.includes('verification code') || text.includes('mã xác minh') ||
                   text.includes('ứng dụng') || text.includes('google prompts') ||
                   text.includes('ứng dụng google');
        })
    );
    const hasSignedInMarker = !!document.querySelector(
        '[data-profile-identifier], [data-ogsr-up], [aria-label*=""Google Account""], ' +
        '[aria-label*=""Google Account:""], [aria-label*=""Sign out""], ' +
        'a[href*=""SignOut""], a[href*=""Logout""], a[href*=""logout""]') ||
        (window.location.host === 'myaccount.google.com' && window.location.pathname === '/' &&
         !document.querySelector('input[type=""email""], input[type=""password""]')) ||
        (window.location.host === 'gds.google.com' && window.location.pathname.includes('/web/landing'));
    const isSignInPage = window.location.pathname.includes('/signin') ||
                         !!document.querySelector('input[type=""email""], input[type=""password""]');
    const hasCompletionSignal = hasSignedInMarker && !isSignInPage;
    const hasManualChallenge = !!document.querySelector('[data-challenge-id], .captcha, #captcha');

    return {
        pageUrl: pageUrl,
        hasEmailField: hasEmailField,
        hasPasswordField: hasPasswordField,
        hasTotpField: hasTotpField,
        has2FAMethodPicker: has2FAMethodPicker,
        hasCompletionSignal: hasCompletionSignal,
        hasManualChallenge: hasManualChallenge,
        emailValue: emailValue
    };
})({})
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        // Log state to console for debugging
        if (result.TryGetProperty("result", out var robj) && robj.TryGetProperty("value", out var vobj))
        {
            try
            {
                var path = vobj.GetProperty("pageUrl").GetString();
                var has2FA = vobj.GetProperty("has2FAMethodPicker").GetBoolean();
                var hasPwd = vobj.GetProperty("hasPasswordField").GetBoolean();
                var hasTotp = vobj.GetProperty("hasTotpField").GetBoolean();
                var hasEmail = vobj.GetProperty("hasEmailField").GetBoolean();
                var emailValue = vobj.TryGetProperty("emailValue", out var ev) ? ev.GetString() : "";
                System.Console.WriteLine($"[ReadState] path={path?.Substring(0, Math.Min(60, path?.Length ?? 0))} Email={hasEmail}({emailValue}) 2FA={has2FA} Pwd={hasPwd} Totp={hasTotp}");
            }
            catch { }
        }

        try
        {
            var value = result.GetProperty("result").GetProperty("value");

            var pageUrl = new Uri(value.GetProperty("pageUrl").GetString()!);
            var hasEmailField = value.GetProperty("hasEmailField").GetBoolean();
            var hasPasswordField = value.GetProperty("hasPasswordField").GetBoolean();
            var hasTotpField = value.GetProperty("hasTotpField").GetBoolean();
            var has2FAMethodPicker = value.GetProperty("has2FAMethodPicker").GetBoolean();
            var hasCompletionSignal = value.GetProperty("hasCompletionSignal").GetBoolean();
            var hasManualChallenge = value.GetProperty("hasManualChallenge").GetBoolean();

            return new GoogleLoginPageState(
                pageUrl,
                hasEmailField,
                hasPasswordField,
                hasTotpField,
                has2FAMethodPicker,
                hasCompletionSignal,
                hasManualChallenge);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException)
        {
            throw new InvalidOperationException($"Failed to read page state: {ex.Message}", ex);
        }
    }

    private static bool IsDiagnosticCaptureEnabled()
    {
        var env = Environment.GetEnvironmentVariable("ROUTERPLUS_LIVE_E2E");
        return !string.IsNullOrEmpty(env) && env != "false";
    }

    private static string? GetDiagnosticFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("ROUTERPLUS_LOG_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        var dir = Path.Combine(baseDir, "RouterPlus");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "google-login-diag.log");
    }

    private async Task CaptureTransitionDiagnosticAsync(
        GoogleLoginField submittedField,
        CancellationToken cancellationToken)
    {
        if (!IsDiagnosticCaptureEnabled())
        {
            return;
        }

        try
        {
            const string probeScript = @"
(function() {
    const host = window.location.host;
    const path = window.location.pathname;
    const search = window.location.search;
    const hash = window.location.hash;
    const isVisible = el => {
        const r = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && r.width > 0 && r.height > 0;
    };
    const countVisible = sel => Array.from(document.querySelectorAll(sel)).filter(isVisible).length;
    const has = sel => countVisible(sel) > 0;
    const buttonCandidates = [
        '#identifierNext', '#passwordNext', '#totpNext',
        '[jsname=""LgbsSe""]', '[jsname=""Njthtb""]',
        '[data-primary-action-label]', 'button[type=""submit""]'
    ].filter(has).length;
    const buttonsByLabel = Array.from(document.querySelectorAll('[role=""button""], button')).filter(b => {
        const t = ((b.innerText || '') + ' ' + (b.getAttribute('aria-label') || '')).toLowerCase();
        return ['next','tiep','tiếp','continue','sign in','đăng nhập','submit','try another way','resend'].some(l => t.includes(l));
    }).length;
    const visibleClickableSample = Array.from(document.querySelectorAll('button, [role=""button""], li, div[onclick], a')).filter(isVisible).slice(0, 15).map(el => {
        return ((el.tagName || '') + ':' + ((el.innerText || '').substring(0, 30)) + ':' + (el.getAttribute('aria-label') || '').substring(0, 30)).replace(/[|\\]/g, '_');
    }).join('|');
    const hasAccountPicker = has('[data-accounts-email], [data-email]') || !!document.querySelector('ul[role=""listbox""]');
    const hasVerifyChallenge = !!document.querySelector('[data-challenge-id], [data-challenge-type], .captcha, #captcha, [aria-label*=""verify"" i], [aria-label*=""recaptcha"" i]');
    const has2FAMethodPicker = !!document.querySelector('[data-challenge-type=""selectChallenge""], [data-second-factor-type], [aria-label*=""2-step"" i], [aria-label*=""two-step"" i]') ||
        window.location.pathname.includes('/challenge/selection');
    const hasPhonePrompt = !!document.querySelector('input[type=""tel""], input[name*=""phone"" i]');
    const hasSecurityKeyOption = !!document.querySelector('[data-challenge-type=""securityKey""], [data-second-factor-type=""SECURITY_KEY""]');
    const alerts = document.querySelectorAll('[role=""alert""], [aria-live=""assertive""]').length;
    const iframes = Array.from(document.querySelectorAll('iframe')).map(f => {
        return (f.getAttribute('name') || '') + '|' + (f.getAttribute('src') || '').slice(0, 80);
    }).join(';');
    const forms = document.querySelectorAll('form').length;
    const inputs = Array.from(document.querySelectorAll('input')).map(i => {
        return (i.getAttribute('type') || '') + ':' + (i.getAttribute('name') || '') + ':' + (i.getAttribute('autocomplete') || '');
    }).join(';');
    const bodyText = (document.body && document.body.innerText || '').toLowerCase();
    const textMarkers = {
        chooseAccount: bodyText.includes('choose an account') || bodyText.includes('chọn tài khoản'),
        signInText: bodyText.includes('sign in') || bodyText.includes('đăng nhập'),
        acceptAll: bodyText.includes('accept all') || bodyText.includes('chấp nhận tất cả'),
        rejectAll: bodyText.includes('reject all') || bodyText.includes('từ chối tất cả'),
        beforeContinue: bodyText.includes('before you continue') || bodyText.includes('trước khi tiếp tục'),
        verifyYou: bodyText.includes(""verify it's you"") || bodyText.includes('xác minh bạn là'),
        notYou: bodyText.includes('not you') || bodyText.includes('không phải bạn'),
        loading: !!document.querySelector('[aria-busy=""true""], [role=""progressbar""]'),
        visibleTextInputs: Array.from(document.querySelectorAll('input[type=""text""], input:not([type])')).filter(isVisible).length,
        visibleButtons: Array.from(document.querySelectorAll('button, [role=""button""]')).filter(isVisible).length
    };
    return {
        host, path, search, hash,
        hasEmailField: has('input[type=""email""]'),
        hasPasswordField: has('input[type=""password""]'),
        hasTotpField: has('input[name=""totpPin""]'),
        buttonCandidates, buttonsByLabel, alerts, forms,
        hasAccountPicker, hasVerifyChallenge, has2FAMethodPicker,
        hasPhonePrompt, hasSecurityKeyOption,
        iframes, inputs, textMarkers, visibleClickableSample
    };
})()
";

            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = probeScript,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            string diagnosticJson;
            if (result.TryGetProperty("result", out var remoteObject)
                && remoteObject.TryGetProperty("value", out var value))
            {
                diagnosticJson = value.GetRawText();
            }
            else
            {
                diagnosticJson = "{\"error\":\"no_value\"}";
            }

            var filePath = GetDiagnosticFilePath();
            if (filePath == null)
            {
                return;
            }

            var line = $"[{DateTimeOffset.UtcNow:O}] field={submittedField} {diagnosticJson}{Environment.NewLine}";
            await File.AppendAllTextAsync(filePath, line, cancellationToken);
        }
        catch
        {
            // Diagnostic capture must never affect production flow.
        }
    }

    /// <summary>
    /// When the Google account chooser page is shown, navigate directly to
    /// the identifier entry page to bypass the chooser.
    /// </summary>
    private async Task<bool> TryBypassAccountChooserAsync(CancellationToken cancellationToken)
    {
        try
        {
            const string script = @"
(function() {
    const path = window.location.pathname;
    if (path !== '/v3/signin/accountchooser' && path !== '/signin/accountchooser') {
        return { navigated: false, path: path };
    }
    const url = new URL(window.location.href);
    url.pathname = '/v3/signin/identifier';
    window.location.href = url.toString();
    return { navigated: true };
})()
";
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (!result.TryGetProperty("result", out var remoteObject)
                || !remoteObject.TryGetProperty("value", out var value))
            {
                return false;
            }

            return value.TryGetProperty("navigated", out var navigated)
                && navigated.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Skip Google home address collection speedbump.
    /// Looks for "Skip", "Not now", "Bỏ qua", "Để sau" buttons and clicks them.
    /// </summary>
    private async Task<bool> TrySkipHomeAddressAsync(CancellationToken cancellationToken)
    {
        try
        {
            System.Console.WriteLine("[GoogleLogin] Detected potential home address speedbump, attempting to skip...");

            const string script = @"
(function() {
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Check if this is home address page
    const bodyText = (document.body.innerText || '').toLowerCase();
    const isHomeAddressPage = bodyText.includes('home address') ||
                               bodyText.includes('địa chỉ nhà') ||
                               bodyText.includes('where do you live');

    if (!isHomeAddressPage) {
        return { found: false, reason: 'not_home_address_page' };
    }

    // Find skip/not-now buttons
    const skipKeywords = ['skip', 'not now', 'bỏ qua', 'để sau', 'remind me later', 'nhắc tôi sau', 'no thanks', 'không cảm ơn'];
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], a'));

    for (const btn of buttons) {
        if (!isVisible(btn)) continue;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        if (skipKeywords.some(keyword => text.includes(keyword))) {
            const rect = btn.getBoundingClientRect();
            return {
                found: true,
                text: (btn.innerText || '').substring(0, 50),
                centerX: rect.left + rect.width / 2,
                centerY: rect.top + rect.height / 2
            };
        }
    }

    return { found: false, reason: 'no_skip_button' };
})()
";
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (!result.TryGetProperty("result", out var remoteObject)
                || !remoteObject.TryGetProperty("value", out var value))
            {
                return false;
            }

            if (value.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.True
                && value.TryGetProperty("centerX", out var centerX)
                && value.TryGetProperty("centerY", out var centerY))
            {
                var text = value.TryGetProperty("text", out var t) ? t.GetString() : "";
                var x = centerX.GetDouble();
                var y = centerY.GetDouble();

                System.Console.WriteLine($"[GoogleLogin] Clicking skip button at ({x:F0}, {y:F0}), text=\"{text}\"");

                // Click using CDP mouse events
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mousePressed",
                    x = x,
                    y = y,
                    button = "left",
                    clickCount = 1
                }, cancellationToken, _sessionId);

                await Task.Delay(50, cancellationToken);

                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseReleased",
                    x = x,
                    y = y,
                    button = "left",
                    clickCount = 1
                }, cancellationToken, _sessionId);

                System.Console.WriteLine("[GoogleLogin] Home address skip button clicked");
                return true;
            }

            if (value.TryGetProperty("reason", out var reason))
            {
                System.Console.WriteLine($"[GoogleLogin] Home address skip not needed: {reason.GetString()}");
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[GoogleLogin] Failed to skip home address: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Skip Google passkey enrollment speedbump after successful authentication.
    /// Looks for "Skip", "Not now", "Bỏ qua", "Để sau" buttons and clicks them.
    /// </summary>
    private async Task<bool> TrySkipPasskeyEnrollmentAsync(CancellationToken cancellationToken)
    {
        try
        {
            System.Console.WriteLine("[GoogleLogin] Detected passkey enrollment page, attempting to skip...");

            const string script = @"
(function() {
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Find skip/not-now buttons
    const skipKeywords = ['skip', 'not now', 'bỏ qua', 'để sau', 'remind me later', 'nhắc tôi sau'];
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], a'));

    for (const btn of buttons) {
        if (!isVisible(btn)) continue;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        if (skipKeywords.some(keyword => text.includes(keyword))) {
            const rect = btn.getBoundingClientRect();
            return {
                found: true,
                text: (btn.innerText || '').substring(0, 50),
                centerX: rect.left + rect.width / 2,
                centerY: rect.top + rect.height / 2
            };
        }
    }

    return { found: false };
})()
";
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (!result.TryGetProperty("result", out var remoteObject)
                || !remoteObject.TryGetProperty("value", out var value))
            {
                return false;
            }

            if (value.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.True
                && value.TryGetProperty("centerX", out var centerX)
                && value.TryGetProperty("centerY", out var centerY))
            {
                var text = value.TryGetProperty("text", out var t) ? t.GetString() : "";
                var x = centerX.GetDouble();
                var y = centerY.GetDouble();

                System.Console.WriteLine($"[GoogleLogin] Clicking skip button at ({x:F0}, {y:F0}), text=\"{text}\"");

                // Click using CDP mouse events
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mousePressed",
                    x = x,
                    y = y,
                    button = "left",
                    clickCount = 1
                }, cancellationToken, _sessionId);

                await Task.Delay(50, cancellationToken);

                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseReleased",
                    x = x,
                    y = y,
                    button = "left",
                    clickCount = 1
                }, cancellationToken, _sessionId);

                System.Console.WriteLine("[GoogleLogin] Passkey enrollment skip button clicked");
                return true;
            }

            System.Console.WriteLine("[GoogleLogin] No skip button found on passkey enrollment page");
            return false;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[GoogleLogin] Failed to skip passkey enrollment: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// When Google shows 2FA method picker, click to select the Authenticator option.
    /// Retries up to 5 times with 500ms delay between attempts to handle page rendering timing.
    /// </summary>
    public async Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
    {
        System.Console.WriteLine("[GoogleLogin] Entering TrySelectAuthenticatorMethodAsync");

        // First, wait for loading spinner to disappear (up to 5 seconds)
        for (int waitAttempt = 1; waitAttempt <= 10; waitAttempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                const string checkLoadingScript = @"
(function() {
    const progressBars = Array.from(document.querySelectorAll('[role=""progressbar""], [aria-label*=""tải"" i], [aria-label*=""loading"" i]'));
    const loadingTexts = Array.from(document.querySelectorAll('*')).filter(el => {
        const text = (el.innerText || el.textContent || '').trim().toLowerCase();
        return text === 'đang tải' || text === 'loading';
    });
    const isLoading = progressBars.length > 0 || loadingTexts.length > 0;
    return { isLoading: isLoading, progressBars: progressBars.length, loadingTexts: loadingTexts.length };
})()
";
                var checkResult = await _client.CallAsync("Runtime.evaluate", new
                {
                    expression = checkLoadingScript,
                    returnByValue = true,
                    awaitPromise = false
                }, cancellationToken, _sessionId);

                if (checkResult.TryGetProperty("result", out var checkResultObj)
                    && checkResultObj.TryGetProperty("value", out var checkValue)
                    && checkValue.TryGetProperty("isLoading", out var isLoadingProp)
                    && isLoadingProp.ValueKind == JsonValueKind.False)
                {
                    System.Console.WriteLine($"[GoogleLogin] Loading complete after {waitAttempt * 500}ms");
                    break;
                }

                if (waitAttempt == 1)
                {
                    System.Console.WriteLine($"[GoogleLogin] Page is loading, waiting for spinner to disappear...");
                }
            }
            catch { }

            await Task.Delay(500, cancellationToken);
        }

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                const string script = @"
(function() {
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Debug: capture ALL clickable elements with their full text and attributes
    const allClickable = Array.from(document.querySelectorAll('li, div[role=""link""], div[role=""button""], button, [role=""radio""], div[data-challengetype], div[data-challenge-type], div[jsname], div[data-challenge-id]'))
        .filter(isVisible)
        .map(el => ({
            tag: el.tagName,
            role: el.getAttribute('role') || '',
            text: (el.innerText || el.textContent || '').substring(0, 100).trim(),
            dataChallenge: el.getAttribute('data-challengetype') || el.getAttribute('data-challenge-type') || '',
            jsname: el.getAttribute('jsname') || '',
            ariaLabel: el.getAttribute('aria-label') || ''
        }));

    // Find clickable elements matching Authenticator/Google prompts/Verification code keywords
    const candidates = Array.from(document.querySelectorAll('li, div[role=""link""], div[role=""button""], button, [role=""radio""], div[data-challengetype], div[data-challenge-type]')).filter(el => {
        const text = (el.innerText || el.textContent || '').toLowerCase();
        const ariaLabel = (el.getAttribute('aria-label') || '').toLowerCase();
        const combinedText = text + ' ' + ariaLabel;
        return isVisible(el) && (combinedText.includes('authenticator') || combinedText.includes('xác thực') ||
               combinedText.includes('verification code') || combinedText.includes('mã xác minh') ||
               combinedText.includes('ứng dụng') || combinedText.includes('google prompts') ||
               combinedText.includes('ứng dụng google') || combinedText.includes('get a verification code') ||
               combinedText.includes('app') && combinedText.includes('code'));
    });

    if (candidates.length === 0) {
        return { clicked: false, reason: 'no_authenticator_option', allClickable: allClickable };
    }

    // Get bounding rect of first matching option for CDP mouse click
    const target = candidates[0];
    const rect = target.getBoundingClientRect();
    const clickedText = (target.innerText || target.textContent || '').substring(0, 100);

    return {
        clicked: false,
        needsCdpClick: true,
        clickedText: clickedText,
        centerX: rect.left + rect.width / 2,
        centerY: rect.top + rect.height / 2
    };
})()
";
                var result = await _client.CallAsync("Runtime.evaluate", new
                {
                    expression = script,
                    returnByValue = true,
                    awaitPromise = false
                }, cancellationToken, _sessionId);

                if (result.TryGetProperty("result", out var remoteObject)
                    && remoteObject.TryGetProperty("value", out var value))
                {
                    // Check if we need CDP mouse click
                    if (value.TryGetProperty("needsCdpClick", out var needsCdp)
                        && needsCdp.ValueKind == JsonValueKind.True
                        && value.TryGetProperty("centerX", out var centerX)
                        && value.TryGetProperty("centerY", out var centerY))
                    {
                        var clickedText = value.TryGetProperty("clickedText", out var ct) ? ct.GetString() : "(no text)";
                        var x = centerX.GetDouble();
                        var y = centerY.GetDouble();

                        System.Console.WriteLine($"[GoogleLogin] Using CDP mouse click at ({x:F0}, {y:F0}), text=\"{clickedText}\"");

                        // Dispatch mouse events via CDP
                        await _client.CallAsync("Input.dispatchMouseEvent", new
                        {
                            type = "mousePressed",
                            x = x,
                            y = y,
                            button = "left",
                            clickCount = 1
                        }, cancellationToken, _sessionId);

                        await Task.Delay(50, cancellationToken);

                        await _client.CallAsync("Input.dispatchMouseEvent", new
                        {
                            type = "mouseReleased",
                            x = x,
                            y = y,
                            button = "left",
                            clickCount = 1
                        }, cancellationToken, _sessionId);

                        System.Console.WriteLine($"[GoogleLogin] CDP mouse click dispatched (attempt {attempt})");
                        return true;
                    }

                    if (value.TryGetProperty("clicked", out var clicked)
                        && clicked.ValueKind == JsonValueKind.True)
                    {
                        var clickedText = value.TryGetProperty("clickedText", out var ct) ? ct.GetString() : "(no text)";
                        System.Console.WriteLine($"[GoogleLogin] Clicked Authenticator method (attempt {attempt}), text=\"{clickedText}\"");
                        return true;
                    }
                }

                System.Console.WriteLine($"[GoogleLogin] Attempt {attempt}: no click result returned");

                if (result.TryGetProperty("result", out var robj)
                    && robj.TryGetProperty("value", out var vobj)
                    && vobj.TryGetProperty("reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String)
                {
                    System.Console.WriteLine($"[GoogleLogin] Attempt {attempt}: {reason.GetString()}");

                    // Log available options for debugging
                    if (vobj.TryGetProperty("allClickable", out var allClickableArray) && allClickableArray.ValueKind == JsonValueKind.Array)
                    {
                        System.Console.WriteLine($"[GoogleLogin] All clickable elements on 2FA page:");
                        foreach (var item in allClickableArray.EnumerateArray())
                        {
                            try
                            {
                                var tag = item.TryGetProperty("tag", out var t) ? t.GetString() : "";
                                var role = item.TryGetProperty("role", out var r) ? r.GetString() : "";
                                var text = item.TryGetProperty("text", out var txt) ? txt.GetString() : "";
                                var dataChallenge = item.TryGetProperty("dataChallenge", out var dc) ? dc.GetString() : "";
                                var jsname = item.TryGetProperty("jsname", out var jn) ? jn.GetString() : "";
                                var ariaLabel = item.TryGetProperty("ariaLabel", out var al) ? al.GetString() : "";

                                System.Console.WriteLine($"  - tag={tag} role={role} text=\"{text}\" dataChallenge={dataChallenge} jsname={jsname} ariaLabel=\"{ariaLabel}\"");
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
                // Continue retry
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    public async Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        await ValidateTargetAsync(cancellationToken);

        var selector = field switch
        {
            GoogleLoginField.Email => "input[type=\"email\"], input[name=\"identifier\"], input[autocomplete*=\"username\" i]",
            GoogleLoginField.Password => "input[type=\"password\"]",
            GoogleLoginField.Totp => "input[name=\"totpPin\"]",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        System.Console.WriteLine($"[Fill] {field} - Finding visible field with selector: {selector}");

        // Use Runtime.evaluate for compatibility with Chromium variants that
        // reject Runtime.callFunctionOn before the document execution context settles.
        var selectorJson = JsonSerializer.Serialize(selector);
        var focusExpression = "(() => { const elements = document.querySelectorAll(" + selectorJson + "); " +
                              "const element = Array.from(elements).find(function(e) { var r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }); " +
                              "if (!element) throw new Error('Visible field not found'); " +
                              "element.focus(); element.click(); " +
                              "return { found: true, tagName: element.tagName, type: element.type, name: element.name || '', id: element.id || '', placeholder: element.placeholder || '' }; })()";

        var focusResult = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = focusExpression,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        if (focusResult.TryGetProperty("exceptionDetails", out var focusException))
        {
            throw new InvalidOperationException(
                $"Failed to focus {field} field: {GetCdpExceptionDescription(focusException)}");
        }

        // Log field info
        if (focusResult.TryGetProperty("result", out var focusResultObj)
            && focusResultObj.TryGetProperty("value", out var focusValue))
        {
            try
            {
                var tagName = focusValue.TryGetProperty("tagName", out var t) ? t.GetString() : "";
                var type = focusValue.TryGetProperty("type", out var ty) ? ty.GetString() : "";
                var name = focusValue.TryGetProperty("name", out var n) ? n.GetString() : "";
                var id = focusValue.TryGetProperty("id", out var i) ? i.GetString() : "";
                var placeholder = focusValue.TryGetProperty("placeholder", out var p) ? p.GetString() : "";
                System.Console.WriteLine($"[Fill] {field} - Found: tagName={tagName} type={type} name={name} id={id} placeholder={placeholder}");
            }
            catch { }
        }

        System.Console.WriteLine($"[Fill] {field} - Field focused, waiting 500ms before clear...");
        await Task.Delay(500, cancellationToken);

        // The application vault is the source of truth for this flow. Clear
        // browser autofill before inserting the vault value.
        System.Console.WriteLine($"[Fill] {field} - Clearing field with Ctrl+A...");
        // Clear existing content with Ctrl+A
        await _client.CallAsync("Input.dispatchKeyEvent", new
        {
            type = "keyDown",
            modifiers = 2, // Ctrl
            key = "a"
        }, cancellationToken, _sessionId);

        await Task.Delay(100, cancellationToken);

        await _client.CallAsync("Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            modifiers = 2,
            key = "a"
        }, cancellationToken, _sessionId);

        await Task.Delay(500, cancellationToken);

        // Check if field is empty or has autofill value
        var checkValueExpression = "(() => { const elements = document.querySelectorAll(" + selectorJson + "); " +
                                   "const element = Array.from(elements).find(function(e) { var r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }); " +
                                   "return element ? { value: element.value, valueLength: element.value.length } : { value: '', valueLength: 0 }; })()";

        var checkResult = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = checkValueExpression,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        if (checkResult.TryGetProperty("result", out var checkResultObj)
            && checkResultObj.TryGetProperty("value", out var checkValue))
        {
            try
            {
                var valueLength = checkValue.TryGetProperty("valueLength", out var vl) ? vl.GetInt32() : 0;
                System.Console.WriteLine($"[Fill] {field} - After Ctrl+A, field value length: {valueLength}");
            }
            catch { }
        }

        // Insert text using Input.insertText (does not use clipboard)
        var maskedValue = field == GoogleLoginField.Password || field == GoogleLoginField.Totp
            ? new string('*', value.Length)
            : value;
        System.Console.WriteLine($"[Fill] {field} - Inserting text (length={value.Length}, masked={maskedValue})...");
        await _client.CallAsync("Input.insertText", new { text = value }, cancellationToken, _sessionId);

        await Task.Delay(1000, cancellationToken);

        // Dispatch input event to trigger framework listeners (Google requires this)
        var inputEventJson = JsonSerializer.Serialize(selector);
        var triggerEventExpression = "(() => { const elements = document.querySelectorAll(" + inputEventJson + "); " +
                                     "const element = Array.from(elements).find(function(e) { var r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }); " +
                                     "if (!element) return false; " +
                                     "element.dispatchEvent(new Event('input', { bubbles: true })); " +
                                     "element.dispatchEvent(new Event('change', { bubbles: true })); " +
                                     "return { triggered: true, finalValueLength: element.value.length }; })()";
        var triggerResult = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = triggerEventExpression,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        // Verify final value
        if (triggerResult.TryGetProperty("result", out var triggerResultObj)
            && triggerResultObj.TryGetProperty("value", out var triggerValue))
        {
            try
            {
                var finalLength = triggerValue.TryGetProperty("finalValueLength", out var fl) ? fl.GetInt32() : 0;
                System.Console.WriteLine($"[Fill] {field} - Events triggered, final value length: {finalLength} (expected: {value.Length})");

                if (finalLength != value.Length)
                {
                    System.Console.WriteLine($"[Fill] {field} - WARNING: Value length mismatch! Field may have been cleared by browser.");
                }

                // Wait to let the value stabilize before submit
                System.Console.WriteLine($"[Fill] {field} - Waiting 1000ms for field to stabilize before submit...");
                await Task.Delay(1000, cancellationToken);
            }
            catch { }
        }
    }

    public async Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await ValidateTargetAsync(cancellationToken);

        var selector = field switch
        {
            GoogleLoginField.Email => "input[type=\"email\"], input[name=\"identifier\"], input[autocomplete*=\"username\" i]",
            GoogleLoginField.Password => "input[type=\"password\"]",
            GoogleLoginField.Totp => "input[name=\"totpPin\"]",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        // Google uses step-specific controls whose exact markup varies by
        // Chromium version and locale.
        var selectorJson = JsonSerializer.Serialize(selector);
        var submitExpression = $"(() => {{ const elements = document.querySelectorAll({selectorJson}); " +
                               "const element = Array.from(elements).find(e => {{ const r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }}); " +
                               "if (!element) throw new Error('Visible field not found'); " +
                               "const fieldKind = Array.from(elements).some(e => e.matches('input[type=\"password\"]')) ? 'password' : " +
                               "(Array.from(elements).some(e => e.matches('input[name=\"totpPin\"]')) ? 'totp' : 'email'); " +
                               "const nextButtonId = fieldKind === 'password' ? '#passwordNext' : " +
                               "fieldKind === 'totp' ? '#totpNext' : '#identifierNext'; " +
                               "const candidates = [nextButtonId, '[jsname=\\\"LgbsSe\\\"]', '[jsname=\\\"Njthtb\\\"]', " +
                               "'[data-primary-action-label]', 'button[type=\\\"submit\\\"]']; " +
                               "let button = null; " +
                               "for (const candidateSelector of candidates) { const candidate = document.querySelector(candidateSelector); " +
                               "if (candidate && !candidate.disabled && candidate.getClientRects().length > 0) { const rect = candidate.getBoundingClientRect(); if (rect.width > 0 && rect.height > 0) { button = candidate; break; } } } " +
                               "if (!button) { const labels = ['next', 'tiếp', 'continue', 'sign in', 'đăng nhập', 'submit']; " +
                               "for (const candidate of document.querySelectorAll('[role=\\\"button\\\"], button')) { " +
                               "const label = ((candidate.innerText || '') + ' ' + (candidate.getAttribute('aria-label') || '')).toLowerCase(); " +
                               "const rect = candidate.getBoundingClientRect(); " +
                               "if (!candidate.disabled && candidate.getClientRects().length > 0 && rect.width > 0 && rect.height > 0 && labels.some(item => label.includes(item))) { button = candidate; break; } } } " +
                               "if (button) { button.click(); " +
                               "const form = element.closest('form'); " +
                               "if (form && form.requestSubmit) { try { form.requestSubmit(); } catch (e) {} } " +
                               "return { submitted: true, method: 'button_click' }; } " +
                               "const form = element.closest('form'); " +
                               "if (form) { form.requestSubmit ? form.requestSubmit() : form.submit(); return { submitted: true, method: 'form_submit' }; } " +
                               "return { submitted: false }; })()";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = submitExpression,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        if (result.TryGetProperty("exceptionDetails", out var submitException))
        {
            throw new InvalidOperationException(
                $"Failed to submit {field} field: {GetCdpExceptionDescription(submitException)}");
        }

        var submitValue = result.GetProperty("result").GetProperty("value");
        var submittedByDom = submitValue.TryGetProperty("submitted", out var submitted)
            && submitted.ValueKind == JsonValueKind.True;
        var method = submitValue.TryGetProperty("method", out var m) ? m.GetString() : "none";

        System.Console.WriteLine($"[Submit] field={field} submittedByDom={submittedByDom} method={method}");

        if (submittedByDom)
        {
            // Find the submit button location and dispatch real mouse events
            await DispatchMouseClickOnSubmitButtonAsync(field, cancellationToken);
            await Task.Delay(500, cancellationToken);
        }
        else
        {
            // No button or form found - use Enter key as fallback
            var fieldSelector = field switch
            {
                GoogleLoginField.Email => "input[type=\"email\"], input[name=\"identifier\"], input[autocomplete*=\"username\" i]",
                GoogleLoginField.Password => "input[type=\"password\"]",
                GoogleLoginField.Totp => "input[name=\"totpPin\"]",
                _ => "input"
            };
            await FocusFieldAsync(fieldSelector, cancellationToken);
            await Task.Delay(200, cancellationToken);
            await PressEnterAsync(cancellationToken);
        }

        // Give browser time to process the click/submit
        await Task.Delay(500, cancellationToken);

        await WaitForNextStateAsync(field, cancellationToken);
    }

    private async Task WaitForNextStateAsync(
        GoogleLoginField submittedField,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        GoogleLoginPageState? lastState = null;
        var stableStateReads = 0;
        var triedSkipPasskey = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var state = await ReadStateOnceAsync(cancellationToken);

                // After TOTP submit, Google may show passkey enrollment speedbump
                if (submittedField == GoogleLoginField.Totp && !triedSkipPasskey &&
                    state.PageUri.Host == "accounts.google.com" &&
                    state.PageUri.AbsolutePath.Contains("/speedbump"))
                {
                    System.Console.WriteLine("[WaitForNextState] Detected speedbump page after TOTP, attempting skip...");
                    if (await TrySkipPasskeyEnrollmentAsync(cancellationToken))
                    {
                        triedSkipPasskey = true;
                        System.Console.WriteLine("[WaitForNextState] Skip succeeded, waiting for navigation...");
                        await Task.Delay(2000, cancellationToken);
                        // Reset deadline to give time for final navigation
                        deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                        continue;
                    }
                }

                var advanced = submittedField switch
                {
                    GoogleLoginField.Email => !state.HasEmailField &&
                        (state.HasPasswordField || state.HasTotpField ||
                         state.HasCompletionSignal || state.HasManualChallenge),
                    GoogleLoginField.Password =>
                        // Password advanced if: password field gone OR 2FA picker appeared (even if password field still visible for back navigation)
                        (!state.HasPasswordField || state.Has2FAMethodPicker) &&
                        (state.HasTotpField || state.HasCompletionSignal ||
                         state.HasManualChallenge || state.Has2FAMethodPicker),
                    GoogleLoginField.Totp => !state.HasTotpField &&
                        (state.HasCompletionSignal || state.HasManualChallenge),
                    _ => false
                };

                if (advanced)
                {
                    if (lastState is not null && state.Equals(lastState))
                    {
                        stableStateReads++;
                    }
                    else
                    {
                        stableStateReads = 1;
                    }

                    lastState = state;
                    if (stableStateReads >= 2)
                    {
                        return;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // The execution context may be recreated during navigation.
            }

            await Task.Delay(100, cancellationToken);
        }

        if (await IsSignInErrorPresentAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Google rejected the {submittedField} submission or requires another verification step.");
        }

        var diagnostics = await ReadSubmitDiagnosticsAsync(submittedField, cancellationToken);
        await CaptureTransitionDiagnosticAsync(submittedField, cancellationToken);
        throw new InvalidOperationException(
            $"Google {submittedField} submit did not advance to the next authentication state ({diagnostics}).");
    }

    private async Task<string> ReadSubmitDiagnosticsAsync(
        GoogleLoginField field,
        CancellationToken cancellationToken)
    {
        var selector = field switch
        {
            GoogleLoginField.Email => "#identifierNext",
            GoogleLoginField.Password => "#passwordNext",
            GoogleLoginField.Totp => "#totpNext",
            _ => string.Empty
        };
        var selectorJson = JsonSerializer.Serialize(selector);
        var expression = $"(() => {{ const b = document.querySelector({selectorJson}); " +
                         "const r = b ? b.getBoundingClientRect() : null; " +
                         "const alerts = document.querySelectorAll('[role=\\\"alert\\\"], [aria-live=\\\"assertive\\\"]'); " +
                         "return 'button=' + !!b + ';disabled=' + (!!b && !!b.disabled) + ';rect=' + " +
                         "(!!r && r.width > 0 && r.height > 0) + ';alerts=' + alerts.length; })()";
        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);
        return result.GetProperty("result").GetProperty("value").GetString() ?? "unavailable";
    }

    private async Task<bool> IsSignInErrorPresentAsync(CancellationToken cancellationToken)
    {
        // Check both body text and alert elements for error keywords
        const string expression = "(() => { " +
                                  "const bodyText = (document.body.innerText || '').toLowerCase(); " +
                                  "const alerts = document.querySelectorAll('[role=\"alert\"], [aria-live=\"assertive\"]'); " +
                                  "let alertText = ''; " +
                                  "alerts.forEach(a => alertText += (a.innerText || '').toLowerCase() + ' '); " +
                                  "const combined = bodyText + ' ' + alertText; " +
                                  "return /wrong password|incorrect password|password is incorrect|couldn't find your google account|could not find your google account|invalid email|wrong email|mật khẩu không đúng|mật khẩu không chính xác|sai mật khẩu|không tìm thấy tài khoản|không chính xác|enter a valid email|enter a valid password/.test(combined); " +
                                  "})()";
        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        return result.TryGetProperty("result", out var remoteObject)
            && remoteObject.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private async Task<bool> IsFieldPresentAsync(string selector, CancellationToken cancellationToken)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = $"!!document.querySelector({selectorJson})",
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        return result.TryGetProperty("result", out var remoteObject)
            && remoteObject.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private async Task DispatchMouseClickOnSubmitButtonAsync(GoogleLoginField field, CancellationToken cancellationToken)
    {
        try
        {
            var fieldSelector = field switch
            {
                GoogleLoginField.Email => "#identifierNext",
                GoogleLoginField.Password => "#passwordNext",
                GoogleLoginField.Totp => "#totpNext",
                _ => "button"
            };

            var selectorJson = JsonSerializer.Serialize(fieldSelector);
            var expression = "(() => { const b = document.querySelector(" + selectorJson + "); " +
                             "if (!b) return null; const r = b.getBoundingClientRect(); " +
                             "return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()";

            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = expression,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (!result.TryGetProperty("result", out var remoteObject)
                || !remoteObject.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var x = value.GetProperty("x").GetDouble();
            var y = value.GetProperty("y").GetDouble();

            // Dispatch real mouse events
            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mousePressed",
                x = x,
                y = y,
                button = "left",
                buttons = 1,
                clickCount = 1
            }, cancellationToken, _sessionId);
            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseReleased",
                x = x,
                y = y,
                button = "left",
                buttons = 1,
                clickCount = 1
            }, cancellationToken, _sessionId);
        }
        catch
        {
            // Best effort
        }
    }

    private async Task FocusFieldAsync(string selector, CancellationToken cancellationToken)
    {
        try
        {
            var selectorJson = JsonSerializer.Serialize(selector);
            var expression = "(() => { const elements = document.querySelectorAll(" + selectorJson + "); " +
                             "const element = Array.from(elements).find(function(e) { var r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }); " +
                             "if (!element) return false; element.focus(); return true; })()";
            await _client.CallAsync("Runtime.evaluate", new
            {
                expression = expression,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);
        }
        catch
        {
            // Best-effort focus
        }
    }

    private async Task PressEnterAsync(CancellationToken cancellationToken)
    {
        await _client.CallAsync("Input.dispatchKeyEvent", new
        {
            type = "keyDown",
            key = "Enter",
            code = "Enter",
            windowsVirtualKeyCode = 13,
            nativeVirtualKeyCode = 13,
            text = "\r",
            unmodifiedText = "\r"
        }, cancellationToken, _sessionId);
        await _client.CallAsync("Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            key = "Enter",
            code = "Enter",
            windowsVirtualKeyCode = 13,
            nativeVirtualKeyCode = 13
        }, cancellationToken, _sessionId);
    }

    private static string GetCdpExceptionDescription(JsonElement exceptionDetails)
    {
        if (exceptionDetails.TryGetProperty("exception", out var exception) &&
            exception.TryGetProperty("description", out var description) &&
            description.ValueKind == JsonValueKind.String)
        {
            return description.GetString() ?? "JavaScript exception.";
        }

        if (exceptionDetails.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.String)
        {
            return text.GetString() ?? "JavaScript exception.";
        }

        return "JavaScript exception.";
    }

    private async Task ValidateTargetAsync(CancellationToken cancellationToken)
    {
        var response = await _client.CallAsync("Target.getTargets", null, cancellationToken);
        var targetInfos = response.GetProperty("targetInfos");

        foreach (var target in targetInfos.EnumerateArray())
        {
            var currentTargetId = target.GetProperty("targetId").GetString();
            if (currentTargetId == _targetId)
            {
                var url = target.GetProperty("url").GetString();
                if (url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    // Allow accounts.google.com, myaccount.google.com, www.google.com, and gds.google.com
                    if (uri.Host != "accounts.google.com" &&
                        uri.Host != "myaccount.google.com" &&
                        uri.Host != "www.google.com" &&
                        uri.Host != "gds.google.com")
                    {
                        throw new InvalidOperationException($"Target navigated to unauthorized host: {uri.Host}");
                    }
                }
                return;
            }
        }

        throw new InvalidOperationException("Target was closed or is no longer available.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Client is owned by caller and will be disposed separately
        await ValueTask.CompletedTask;
    }
}
