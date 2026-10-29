using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Storage;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP adapter that implements IGoogleLoginBrowser for Google login automation.
/// </summary>
internal sealed class GoogleLoginCdpBrowser : IGoogleLoginBrowser
{
    private readonly IChromeCdpClient _client;
    private readonly string _sessionId;
    private readonly string _targetId;
    private bool _disposed;

    public GoogleLoginCdpBrowser(IChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client;
        _sessionId = sessionId;
        _targetId = targetId;
    }

    public async Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
    {
        using var renderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        renderCts.CancelAfter(TimeSpan.FromSeconds(30));
        var triedSkipPasskey = false;
        var triedSkipHomeAddress = false;
        var triedConfirmIdentifier = false;
        var triedRecaptcha = false;
        var emptyPageAttempts = 0;

        try
        {
            while (true)
            {
                var state = await ReadStateOnceAsync(renderCts.Token);

                // Log every loop iteration to track what's happening
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "GoogleLogin",
                    "ReadStateLoop",
                    "ReadState loop iteration",
                    new {
                        page_url = state.PageUri.AbsolutePath,
                        has_email = state.HasEmailField,
                        has_password = state.HasPasswordField,
                        has_totp = state.HasTotpField,
                        has_completion = state.HasCompletionSignal,
                        has_challenge = state.HasManualChallenge
                    });

                // Google reCAPTCHA challenge - must check BEFORE returning on has_challenge
                // because recaptcha page sets has_challenge=true
                if (!triedRecaptcha && state.PageUri.Host == "accounts.google.com" &&
                    state.PageUri.AbsolutePath.Contains("/recaptcha"))
                {
                    triedRecaptcha = true;
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Warning,
                        "GoogleLogin",
                        "RecaptchaDetected",
                        "Detected reCAPTCHA challenge - requires manual user intervention",
                        new { page_url = state.PageUri.AbsolutePath });

                    // Don't auto-click - let HasManualChallenge flag handle it
                    // Auto-clicking is blocked by Google's anti-bot detection
                }

                if (state.HasEmailField || state.HasPasswordField || state.HasTotpField ||
                    state.HasCompletionSignal || state.HasManualChallenge || state.HasAccountChooser || state.HasConsentScreen)
                {
                    return state;
                }

                // Detect completely empty page (Google rendering bug) and force reload
                var isEmpty = !state.HasEmailField && !state.HasPasswordField && !state.HasTotpField
                    && !state.HasCompletionSignal && !state.HasManualChallenge && !state.Has2FAMethodPicker;
                if (isEmpty && state.PageUri.Host == "accounts.google.com" && emptyPageAttempts < 2)
                {
                    emptyPageAttempts++;
                    // DebugConsole.WriteLine($"[ReadState] Empty page detected (attempt {emptyPageAttempts}), reloading...");
                    // Use Runtime.evaluate instead of Page.reload for CentBrowser compatibility
                    await _client.CallAsync("Runtime.evaluate", new
                    {
                        expression = "window.location.reload(true)",
                        awaitPromise = false
                    }, renderCts.Token, _sessionId);
                    await Task.Delay(2000, renderCts.Token);
                    continue;
                }

                // Google account chooser and OAuth consent handling lives in
                // GoogleLoginStateMachine, which knows the expected email address.
                // Both states are returned above, so nothing to do here.

                // Google v3 confirmidentifier page - email already filled, just need to click Continue
                if (!triedConfirmIdentifier && state.PageUri.Host == "accounts.google.com" &&
                    state.PageUri.AbsolutePath.Contains("/confirmidentifier"))
                {
                    // DebugConsole.WriteLine("[ReadState] Detected confirmidentifier page, clicking Continue...");
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "GoogleLogin",
                        "ConfirmIdentifierDetected",
                        "Detected confirmidentifier page in ReadState",
                        new { page_url = state.PageUri.AbsolutePath });

                    if (await TryClickConfirmIdentifierContinueAsync(renderCts.Token))
                    {
                        triedConfirmIdentifier = true;
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "GoogleLogin",
                            "ConfirmIdentifierContinueClicked",
                            "Continue button clicked on confirmidentifier page in ReadState");

                        // DebugConsole.WriteLine("[ReadState] Continue clicked, waiting for navigation...");
                        await Task.Delay(3000, renderCts.Token);

                        // Check if still stuck on confirmidentifier page
                        var checkState = await ReadStateOnceAsync(renderCts.Token);
                        if (checkState.PageUri.AbsolutePath.Contains("/confirmidentifier"))
                        {
                            ObservabilityHub.Instance.LogEvent(
                                LogLevel.Warning,
                                "GoogleLogin",
                                "ConfirmIdentifierStuck",
                                "Page still on confirmidentifier after Continue click - Google anti-bot detection blocking automation");
                        }
                    }

                    continue;
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
    const hasTotpError = Array.from(document.querySelectorAll('div, span, p')).some(el => {
        const text = (el.innerText || el.textContent || '').toLowerCase();
        return text.includes('wrong code') || text.includes('incorrect code') ||
               text.includes('invalid code') || text.includes('mã không đúng') ||
               text.includes('mã sai') || text.includes('try again');
    });
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
    const path = window.location.pathname.toLowerCase();
    const bodyText = (document.body && document.body.innerText || '').toLowerCase();
    const hasNoCredentialField = !hasEmailField && !hasPasswordField && !hasTotpField;
    // data-profile-identifier is Google's signed-in avatar marker and shows up on every
    // signed-in Google page, so it must not be treated as an account-chooser row.
    const hasAccountChooser = window.location.host === 'accounts.google.com' && hasNoCredentialField &&
        (path.includes('accountchooser') || path.includes('account-chooser') ||
         !!document.querySelector('li[data-authuser], [data-authuser], li[data-identifier], [data-identifier][data-email], [data-account]') ||
         Array.from(document.querySelectorAll('div[role=""button""], li[role=""button""]')).some(el => {
             const t = ((el.innerText || el.getAttribute('aria-label') || '')).toLowerCase();
             return (t.includes('@') && t.includes('gmail')) || el.querySelector('[data-identifier], [data-email]');
         }));
    const hasConsentScreen = window.location.host === 'accounts.google.com' && hasNoCredentialField &&
        (path.includes('consent') || path.includes('oauth') ||
         bodyText.includes('choose what to allow') || bodyText.includes('muốn cho phép') ||
         bodyText.includes('continue to') || bodyText.includes('tiếp tục đến') ||
         bodyText.includes('truy cập vào') || bodyText.includes('share your') ||
         bodyText.includes('chỉ định') || bodyText.includes('xem và quản lý') ||
         Array.from(document.querySelectorAll('button, [role=""button""]')).some(b => {
            const t = ((b.innerText || b.getAttribute('aria-label') || '')).toLowerCase();
            return (t.includes('allow') || t.includes('cho phép') || t.includes('đồng ý') || t.includes('accept')) && !t.includes('cancel') && !t.includes('hủy');
        }));
    const hasCompletionSignal = hasSignedInMarker && !isSignInPage && !hasAccountChooser && !hasConsentScreen;
    const hasChallengeElement = !!document.querySelector('[data-challenge-id], .captcha, #captcha');
    const hasManualChallenge = hasChallengeElement || window.location.pathname.includes('/challenge/recaptcha');

    return {
        pageUrl: pageUrl,
        hasEmailField: hasEmailField,
        hasPasswordField: hasPasswordField,
        hasTotpField: hasTotpField,
        hasTotpError: hasTotpError,
        has2FAMethodPicker: has2FAMethodPicker,
        hasCompletionSignal: hasCompletionSignal,
        hasManualChallenge: hasManualChallenge,
        emailValue: emailValue,
        hasAccountChooser: hasAccountChooser,
        hasConsentScreen: hasConsentScreen
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
                // DebugConsole.WriteLine($"[ReadState] path={path?.Substring(0, Math.Min(60, path?.Length ?? 0))} Email={hasEmail} 2FA={has2FA} Pwd={hasPwd} Totp={hasTotp}");
            }
            catch
            {
                // Intentionally swallow: optional debug diagnostic, not critical to login flow
            }
        }

        try
        {
            var value = result.GetProperty("result").GetProperty("value");

            var pageUrl = new Uri(value.GetProperty("pageUrl").GetString()!);
            var hasEmailField = value.GetProperty("hasEmailField").GetBoolean();
            var hasPasswordField = value.GetProperty("hasPasswordField").GetBoolean();
            var hasTotpField = value.GetProperty("hasTotpField").GetBoolean();
            var hasTotpError = value.TryGetProperty("hasTotpError", out var totpErr) && totpErr.GetBoolean();
            var has2FAMethodPicker = value.GetProperty("has2FAMethodPicker").GetBoolean();
            var hasCompletionSignal = value.GetProperty("hasCompletionSignal").GetBoolean();
            var hasManualChallenge = IsManualChallenge(
                pageUrl,
                value.GetProperty("hasManualChallenge").GetBoolean());
            var hasAccountChooser = value.TryGetProperty("hasAccountChooser", out var acct) && acct.GetBoolean();
            var hasConsentScreen = value.TryGetProperty("hasConsentScreen", out var consent) && consent.GetBoolean();

            // Log TOTP error detection
            if (hasTotpError)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "GoogleLogin",
                    "TotpErrorDetected",
                    "TOTP code error detected - wrong or expired code",
                    new { page_url = pageUrl.AbsolutePath });
            }

            return new GoogleLoginPageState(
                pageUrl,
                hasEmailField,
                hasPasswordField,
                hasTotpField,
                hasTotpError,
                has2FAMethodPicker,
                hasCompletionSignal,
                hasManualChallenge,
                hasAccountChooser,
                hasConsentScreen);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException)
        {
            throw new InvalidOperationException($"Failed to read page state: {ex.Message}", ex);
        }
    }

    internal static bool IsManualChallenge(Uri pageUri, bool hasChallengeElement)
    {
        ArgumentNullException.ThrowIfNull(pageUri);
        return hasChallengeElement || pageUri.AbsolutePath.Contains(
            "/challenge/recaptcha",
            StringComparison.OrdinalIgnoreCase);
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

        var path = AppPaths.DiagnosticLogPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return path;
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
    const visibleClickableCount = Array.from(document.querySelectorAll('button, [role=""button""], li, div[onclick], a')).filter(isVisible).length;
    const hasAccountPicker = has('[data-accounts-email], [data-email]') || !!document.querySelector('ul[role=""listbox""]');
    const hasVerifyChallenge = !!document.querySelector('[data-challenge-id], [data-challenge-type], .captcha, #captcha, [aria-label*=""verify"" i], [aria-label*=""recaptcha"" i]');
    const has2FAMethodPicker = !!document.querySelector('[data-challenge-type=""selectChallenge""], [data-second-factor-type], [aria-label*=""2-step"" i], [aria-label*=""two-step"" i]') ||
        window.location.pathname.includes('/challenge/selection');
    const hasPhonePrompt = !!document.querySelector('input[type=""tel""], input[name*=""phone"" i]');
    const hasSecurityKeyOption = !!document.querySelector('[data-challenge-type=""securityKey""], [data-second-factor-type=""SECURITY_KEY""]');
    const alerts = document.querySelectorAll('[role=""alert""], [aria-live=""assertive""]').length;
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
        iframes: (Array.from(document.querySelectorAll('iframe')).filter(isVisible).length),
        inputs, textMarkers, visibleClickableCount
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

            // Redact the full line (timestamp + field + diagnostic JSON) before it is
            // written so the artifact is clean by construction.
            var line = $"[{DateTimeOffset.UtcNow:O}] field={submittedField} {diagnosticJson}{Environment.NewLine}";
            await File.AppendAllTextAsync(filePath, DiagnosticRedactor.Redact(line), cancellationToken);
        }
        catch
        {
            // Diagnostic capture must never affect production flow.
        }
    }

    public async Task<bool> TrySelectAccountOnChooserAsync(string? email, CancellationToken cancellationToken)
    {
        try
        {
            var emailNormalized = string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
            var emailJson = JsonSerializer.Serialize(emailNormalized);
            var script = @"
(function() {
    const wanted = " + emailJson + @";
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const area = el => { const r = el.getBoundingClientRect(); return r.width * r.height; };
    const smallest = list => { list.sort((a, b) => area(a) - area(b)); return list[0] || null; };
    const textOf = el => ((el.innerText || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('title') || '')).toLowerCase();
    const attributesOf = el => {
        const attrs = [];
        const candidates = [el];
        // Also check children for data attributes
        try {
            el.querySelectorAll('[data-identifier],[data-email],[data-authuser]').forEach(c => candidates.push(c));
        } catch (e) {}
        for (const node of candidates) {
            const dId = (node.getAttribute('data-identifier') || '').trim().toLowerCase();
            const dEmail = (node.getAttribute('data-email') || '').trim().toLowerCase();
            const dAuth = (node.getAttribute('data-authuser') || '').trim().toLowerCase();
            if (dId) attrs.push(dId);
            if (dEmail) attrs.push(dEmail);
            if (dAuth && dAuth.includes('@')) attrs.push(dAuth);
        }
        return attrs.filter(Boolean);
    };

    // Account rows. Try to find clickable account entries.
    let rows = Array.from(document.querySelectorAll('[data-authuser], li[data-identifier], [data-identifier], [data-email], div[data-email], [role=""button""][data-account], [role=""link""][data-account]'))
        .filter(el => isVisible(el));
    
    // Filter out elements that have li children (to get leaf-ish rows)
    rows = rows.filter(el => el.querySelectorAll('li').length === 0);
    
    // Also look for common account chooser patterns
    if (rows.length === 0) {
        rows = Array.from(document.querySelectorAll('li[role=""button""], div[role=""button""], [jsname]'))
            .filter(el => isVisible(el) && el.querySelectorAll('li').length === 0)
            .filter(el => {
                const t = textOf(el);
                return t.includes('@') || el.querySelector('[data-identifier], [data-email]');
            });
    }

    let target = null;
    let strategy = 'no_match';

    if (wanted) {
        // Try exact attribute match first (prefer exact email match if multiple)
        target = rows.find(el => attributesOf(el).includes(wanted)) || null;
        if (target) strategy = 'email_attribute';
        if (!target) {
            // Try text match - look for email as token
            const byText = rows.filter(el => {
                const t = (el.innerText || '').toLowerCase();
                // Check if email appears as a word/token
                const tokens = t.split(/[^a-z0-9@._+-]+/);
                if (tokens.includes(wanted)) return true;
                // Also check if it contains the full email
                return t.includes(wanted);
            });
            if (byText.length > 0) {
                target = smallest(byText);
                strategy = 'email_text';
            }
        }
    }

    // No exact match: add the account explicitly instead of signing in a different one.
    if (!target) {
        const anotherCandidates = Array.from(document.querySelectorAll('button, [role=""button""], a, [role=""link""], li, div[jsname], [jsaction]'))
            .filter(el => isVisible(el))
            .filter(el => el.querySelectorAll('li').length === 0 || el.tagName === 'BUTTON' || el.getAttribute('role') === 'button')
            .filter(el => {
                const t = textOf(el);
                return t.includes('use another account') || t.includes('tài khoản khác') || 
                       t.includes('add account') || t.includes('thêm tài khoản') ||
                       t.includes('sign in with different account');
            });
        if (anotherCandidates.length > 0) {
            target = smallest(anotherCandidates);
            if (target) strategy = 'use_another_account';
        }
    }

    // Also try more specific selectors for 'Use another account'
    if (!target && wanted) {
        const moreSpecific = Array.from(document.querySelectorAll('div[role=""link""], li[role=""link""], [data-action=""add-account""]'))
            .filter(el => isVisible(el))
            .filter(el => {
                const t = textOf(el);
                return t.includes('use another') || t.includes('tài khoản khác') || t.includes('another account');
            });
        if (moreSpecific.length > 0) {
            target = smallest(moreSpecific);
            if (target) strategy = 'use_another_account';
        }
    }

    // Caller supplied no email to match, so any listed account is acceptable.
    if (!target && !wanted) {
        if (rows.length > 0) {
            target = smallest(rows.slice());
            if (target) strategy = 'first_account';
        }
    }

    if (!target) return { found: false, strategy: strategy };
    const rect = target.getBoundingClientRect();
    // Ensure element is in view and clickable
    try { target.scrollIntoView({ block: 'center', inline: 'center' }); } catch(e) {}
    const rect2 = target.getBoundingClientRect();
    return { found: true, strategy: strategy, centerX: rect2.left + rect2.width / 2, centerY: rect2.top + rect2.height / 2 };
})()
";
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);
            if (result.TryGetProperty("exceptionDetails", out var chooserException))
            {
                // A script error here would otherwise look like "no matching account".
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error,
                    "GoogleLogin",
                    "AccountChooserScriptFailed",
                    "Account chooser evaluation failed",
                    new { error = GetCdpExceptionDescription(chooserException) });
                return false;
            }

            if (!result.TryGetProperty("result", out var remoteObject) || !remoteObject.TryGetProperty("value", out var value))
            {
                return false;
            }

            var strategy = value.TryGetProperty("strategy", out var strategyProp)
                ? strategyProp.GetString() ?? "unknown"
                : "unknown";

            if (!(value.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.True) ||
                !value.TryGetProperty("centerX", out var cx) || !value.TryGetProperty("centerY", out var cy))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "GoogleLogin",
                    "AccountChooserNoTarget",
                    "Account chooser shown but no account could be selected",
                    new { strategy });
                return false;
            }

            var x = cx.GetDouble();
            var y = cy.GetDouble();

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "GoogleLogin",
                "AccountChooserSelected",
                "Account chooser target selected",
                new { strategy, x, y });

            await _client.CallAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y, button = "none", buttons = 0 }, cancellationToken, _sessionId);
            await _client.CallAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, cancellationToken, _sessionId);
            await Task.Delay(50, cancellationToken);
            await _client.CallAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, cancellationToken, _sessionId);
            return true;
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
            // DebugConsole.WriteLine("[GoogleLogin] Detected potential home address speedbump, attempting to skip...");

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
                var x = centerX.GetDouble();
                var y = centerY.GetDouble();

                // DebugConsole.WriteLine($"[GoogleLogin] Clicking skip button at ({x:F0}, {y:F0})");

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

                // DebugConsole.WriteLine("[GoogleLogin] Home address skip button clicked");
                return true;
            }

            if (value.TryGetProperty("reason", out var reason))
            {
                // DebugConsole.WriteLine($"[GoogleLogin] Home address skip not needed: {reason.GetString()}");
            }

            return false;
        }
        catch (Exception)
        {
            // DebugConsole.WriteLine($"[GoogleLogin] Failed to skip home address: {ex.Message}");
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
            // DebugConsole.WriteLine("[GoogleLogin] Detected passkey enrollment page, attempting to skip...");

            const string script = @"
(function() {
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Find skip/not-now buttons
    const skipKeywords = ['skip', 'not now', 'bỏ qua', 'để sau', 'remind me later', 'nhắc tôi sau', 'maybe later', 'no thanks'];
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], a'));

    // Diagnostic: count visible buttons only; never capture their text or labels.
    const allButtons = buttons.filter(isVisible).map(btn => ({ tag: btn.tagName }));

    for (const btn of buttons) {
        if (!isVisible(btn)) continue;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        if (skipKeywords.some(keyword => text.includes(keyword))) {
            const rect = btn.getBoundingClientRect();
            return {
                found: true,
                centerX: rect.left + rect.width / 2,
                centerY: rect.top + rect.height / 2,
                allButtons: allButtons
            };
        }
    }

    return { found: false, allButtons: allButtons };
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
                var x = centerX.GetDouble();
                var y = centerY.GetDouble();

                // DebugConsole.WriteLine($"[GoogleLogin] Clicking skip button at ({x:F0}, {y:F0})");

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

                // DebugConsole.WriteLine("[GoogleLogin] Passkey enrollment skip button clicked");
                return true;
            }

            // Dump available buttons for debugging
            if (value.TryGetProperty("allButtons", out var allButtons) && allButtons.ValueKind == JsonValueKind.Array)
            {
                // Log only a count of visible buttons; never their text or labels.
                // DebugConsole.WriteLine($"[GoogleLogin] No skip button found. Visible buttons: {allButtons.GetArrayLength()}");
            }
            else
            {
                // DebugConsole.WriteLine("[GoogleLogin] No skip button found on passkey enrollment page");
            }
            return false;
        }
        catch (Exception)
        {
            // DebugConsole.WriteLine($"[GoogleLogin] Failed to skip passkey enrollment: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// When Google shows 2FA method picker, click to select the Authenticator option.
    /// Retries up to 5 times with 500ms delay between attempts to handle page rendering timing.
    /// </summary>
    public async Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
    {
        // DebugConsole.WriteLine("[GoogleLogin] Entering TrySelectAuthenticatorMethodAsync");

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
                    // DebugConsole.WriteLine($"[GoogleLogin] Loading complete after {waitAttempt * 500}ms");
                    break;
                }

                if (waitAttempt == 1)
                {
                    // DebugConsole.WriteLine($"[GoogleLogin] Page is loading, waiting for spinner to disappear...");
                }
            }
            catch
            {
                // Intentionally swallow: spinner check is optional, page may load without spinner
            }

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

    // Debug: count clickable elements only; never capture their text or labels.
    const allClickable = Array.from(document.querySelectorAll('li, div[role=""link""], div[role=""button""], button, [role=""radio""], div[data-challengetype], div[data-challenge-type], div[jsname], div[data-challenge-id]'))
        .filter(isVisible)
        .map(el => ({ tag: el.tagName, role: el.getAttribute('role') || '' }));

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

    return {
        clicked: false,
        needsCdpClick: true,
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
                        var x = centerX.GetDouble();
                        var y = centerY.GetDouble();

                        // DebugConsole.WriteLine($"[GoogleLogin] Using CDP mouse click at ({x:F0}, {y:F0})");

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

                        // DebugConsole.WriteLine($"[GoogleLogin] CDP mouse click dispatched (attempt {attempt})");
                        return true;
                    }

                    if (value.TryGetProperty("clicked", out var clicked)
                        && clicked.ValueKind == JsonValueKind.True)
                    {
                        // DebugConsole.WriteLine($"[GoogleLogin] Clicked Authenticator method (attempt {attempt})");
                        return true;
                    }
                }

                // DebugConsole.WriteLine($"[GoogleLogin] Attempt {attempt}: no click result returned");

                if (result.TryGetProperty("result", out var robj)
                    && robj.TryGetProperty("value", out var vobj)
                    && vobj.TryGetProperty("reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String)
                {
                    // DebugConsole.WriteLine($"[GoogleLogin] Attempt {attempt}: {reason.GetString()}");

                    // Log only a count of clickable elements; never their text or labels.
                    if (vobj.TryGetProperty("allClickable", out var allClickableArray) && allClickableArray.ValueKind == JsonValueKind.Array)
                    {
                        // DebugConsole.WriteLine($"[GoogleLogin] Clickable elements on 2FA page: {allClickableArray.GetArrayLength()}");
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

        // DebugConsole.WriteLine($"[Fill] {field} - Finding visible field with selector: {selector}");

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
                // DebugConsole.WriteLine($"[Fill] {field} - Found: tagName={tagName} type={type} name={name} id={id} placeholder={placeholder}");
            }
            catch
            {
                // Intentionally swallow: optional debug info about focused element
            }
        }

        // DebugConsole.WriteLine($"[Fill] {field} - Field focused, waiting 500ms before clear...");
        await Task.Delay(500, cancellationToken);

        // The application vault is the source of truth for this flow. Clear
        // browser autofill before inserting the vault value.
        // DebugConsole.WriteLine($"[Fill] {field} - Clearing field with Ctrl+A...");
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
                // DebugConsole.WriteLine($"[Fill] {field} - After Ctrl+A, field value length: {valueLength}");
            }
            catch
            {
                // Intentionally swallow: optional debug info about field value length
            }
        }

        // Insert text using Input.insertText (does not use clipboard)
        // DebugConsole.WriteLine($"[Fill] {field} - Inserting text (length={value.Length})...");
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
                // DebugConsole.WriteLine($"[Fill] {field} - Events triggered, final value length: {finalLength} (expected: {value.Length})");

                if (finalLength != value.Length)
                {
                    // DebugConsole.WriteLine($"[Fill] {field} - WARNING: Value length mismatch! Field may have been cleared by browser.");
                }

                // Wait to let the value stabilize before submit
                // DebugConsole.WriteLine($"[Fill] {field} - Waiting 1000ms for field to stabilize before submit...");
                await Task.Delay(1000, cancellationToken);
            }
            catch
            {
                // Intentionally swallow: optional debug info about final value length
            }
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

        var selectorJson = JsonSerializer.Serialize(selector);

        // Wait (up to 10s) for the visible input field and a visible submit button
        // to be present before evaluating submitExpression.
        var submitWaitDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < submitWaitDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var submitReadyExpression = $"(() => {{ " +
                "const elements = document.querySelectorAll(" + selectorJson + "); " +
                "const fieldEl = Array.from(elements).find(e => {{ const r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }}); " +
                "if (!fieldEl) return false; " +
                "const fieldKind = Array.from(elements).some(e => e.matches('input[type=\"password\"]')) ? 'password' : " +
                "(Array.from(elements).some(e => e.matches('input[name=\"totpPin\"]')) ? 'totp' : 'email'); " +
                "const nextButtonId = fieldKind === 'password' ? '#passwordNext' : " +
                "fieldKind === 'totp' ? '#totpNext' : '#identifierNext'; " +
                "const candidates = [nextButtonId, '[jsname=\\\"LgbsSe\\\"]', '[jsname=\\\"Njthtb\\\"]', " +
                "'[data-primary-action-label]', 'button[type=\\\"submit\\\"]']; " +
                "for (const candidateSelector of candidates) {{ const candidate = document.querySelector(candidateSelector); " +
                "if (candidate && !candidate.disabled && candidate.getClientRects().length > 0) {{ const rect = candidate.getBoundingClientRect(); if (rect.width > 0 && rect.height > 0) return true; }} }} " +
                "const labels = ['next', 'tiếp', 'continue', 'sign in', 'đăng nhập', 'submit']; " +
                "for (const candidate of document.querySelectorAll('[role=\\\"button\\\"], button')) {{ " +
                "const label = ((candidate.innerText || '') + ' ' + (candidate.getAttribute('aria-label') || '')).toLowerCase(); " +
                "const rect = candidate.getBoundingClientRect(); " +
                "if (!candidate.disabled && candidate.getClientRects().length > 0 && rect.width > 0 && rect.height > 0 && labels.some(item => label.includes(item))) return true; }} " +
                "return false; })()";
            try
            {
                var readyResult = await _client.CallAsync("Runtime.evaluate", new
                {
                    expression = submitReadyExpression,
                    returnByValue = true,
                    awaitPromise = false
                }, cancellationToken, _sessionId);
                if (readyResult.TryGetProperty("result", out var readyResultObj)
                    && readyResultObj.TryGetProperty("value", out var readyValue)
                    && readyValue.ValueKind == JsonValueKind.True)
                {
                    break;
                }
            }
            catch
            {
                // Ignore transient evaluation failures while waiting
            }
            await Task.Delay(250, cancellationToken);
        }

        // Wait a bit to ensure field is still visible after fill
        // DebugConsole.WriteLine($"[Submit] {field} - Waiting 300ms before submit...");
        await Task.Delay(300, cancellationToken);

        // Log page state BEFORE submit attempt
        await LogPageStateForDebugAsync(field, cancellationToken);

        // Google uses step-specific controls whose exact markup varies by
        // Chromium version and locale.
        var submitExpression = $"(() => {{ const elements = document.querySelectorAll({selectorJson}); " +
                               "const element = Array.from(elements).find(e => {{ const r = e.getBoundingClientRect(); return e.getClientRects().length > 0 && r.width > 0 && r.height > 0; }}); " +
                               "if (!element) {{ console.log('Submit: Field not found. Selector:', {selectorJson}, 'Elements found:', elements.length); throw new Error('Visible field not found'); }} " +
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

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "GoogleLogin",
            "SubmitResult",
            "Submit evaluated",
            new { field = field.ToString(), submittedByDom, method, submitValue = submitValue.ToString() });

        if (submittedByDom)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "GoogleLogin",
                "DispatchMouseClickAttempt",
                "Dispatching trusted mouse click on submit button",
                new { field = field.ToString() });
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
        var triedConfirmIdentifier = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var state = await ReadStateOnceAsync(cancellationToken);

                // Handle Google v3 confirmidentifier page after password submit
                if (submittedField == GoogleLoginField.Password && !triedConfirmIdentifier &&
                    state.PageUri.Host == "accounts.google.com" &&
                    state.PageUri.AbsolutePath.Contains("/confirmidentifier"))
                {
                    // DebugConsole.WriteLine("[WaitForNextState] Detected confirmidentifier page after password, clicking Continue...");
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "GoogleLogin",
                        "ConfirmIdentifierDetected",
                        "Detected confirmidentifier page after password submit",
                        new { submitted_field = submittedField.ToString(), page_url = state.PageUri.AbsolutePath });

                    if (await TryClickConfirmIdentifierContinueAsync(cancellationToken))
                    {
                        triedConfirmIdentifier = true;
                        // DebugConsole.WriteLine("[WaitForNextState] Continue clicked, waiting for navigation...");
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "GoogleLogin",
                            "ConfirmIdentifierContinueClicked",
                            "Continue button clicked on confirmidentifier page");
                        await Task.Delay(1500, cancellationToken);
                        // Reset deadline to give time for navigation
                        deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                        continue;
                    }
                }

                // After TOTP submit, Google may show passkey enrollment speedbump
                if (submittedField == GoogleLoginField.Totp && !triedSkipPasskey &&
                    state.PageUri.Host == "accounts.google.com" &&
                    state.PageUri.AbsolutePath.Contains("/speedbump"))
                {
                    // DebugConsole.WriteLine("[WaitForNextState] Detected speedbump page after TOTP, attempting skip...");
                    if (await TrySkipPasskeyEnrollmentAsync(cancellationToken))
                    {
                        triedSkipPasskey = true;
                        // DebugConsole.WriteLine("[WaitForNextState] Skip succeeded, waiting for navigation...");
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
                         state.HasCompletionSignal || state.HasManualChallenge ||
                         // An existing Google session can skip straight to consent/chooser.
                         state.HasAccountChooser || state.HasConsentScreen),
                    GoogleLoginField.Password =>
                        // Password advanced if: password field gone OR 2FA picker appeared (even if password field still visible for back navigation)
                        // Also treat a consent/account chooser/interstitial page (no password/email fields) as advanced so OAuth can be handled.
                        (!state.HasPasswordField || state.Has2FAMethodPicker) &&
                        (state.HasTotpField || state.HasCompletionSignal ||
                         state.HasManualChallenge || state.Has2FAMethodPicker ||
                         state.HasAccountChooser || state.HasConsentScreen ||
                         (!state.HasPasswordField && !state.HasEmailField)),
                    GoogleLoginField.Totp => !state.HasTotpField &&
                        (state.HasCompletionSignal || state.HasManualChallenge ||
                         state.HasConsentScreen || state.HasAccountChooser),
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
                         "const vb = Array.from(document.querySelectorAll('button, [role=button], [jsname]')).filter(e => { const rr = e.getBoundingClientRect(); return rr.width > 0 && rr.height > 0; }).map(e => { const rr = e.getBoundingClientRect(); return e.tagName.toLowerCase() + ':' + (e.id || '') + ':' + (e.getAttribute('jsname') || '') + ':' + (e.innerText || '').trim().replace(/\\|/g, '') + '@' + rr.left + ',' + rr.top; }).join('|'); " +
                         "return 'button=' + !!b + ';disabled=' + (!!b && !!b.disabled) + ';rect=' + " +
                         "(!!r && r.width > 0 && r.height > 0) + ';alerts=' + alerts.length + ';visibleButtons=' + vb; })()";
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
            var primarySelector = field switch
            {
                GoogleLoginField.Email => "#identifierNext",
                GoogleLoginField.Password => "#passwordNext",
                GoogleLoginField.Totp => "#totpNext",
                _ => "button"
            };

            var primarySelectorJson = JsonSerializer.Serialize(primarySelector);
            var expression = "(() => {" +
                             "const primary = " + primarySelectorJson + ";" +
                             "const candidates = [primary, '[jsname=\"LgbsSe\"]', '[jsname=\"Njthtb\"]', '[data-primary-action-label]', 'button[type=\"submit\"]'];" +
                             "let button = null;" +
                             "for (const selector of candidates) { const candidate = document.querySelector(selector); if (candidate && !candidate.disabled && candidate.getClientRects().length > 0) { const rect = candidate.getBoundingClientRect(); if (rect.width > 0 && rect.height > 0) { button = candidate; break; } } }" +
                             "if (!button) { const labels = ['next', 'tiếp', 'continue', 'sign in', 'đăng nhập', 'submit'];" +
                             "for (const candidate of document.querySelectorAll('[role=\"button\"], button')) {" +
                             "const label = ((candidate.innerText || '') + ' ' + (candidate.getAttribute('aria-label') || '')).toLowerCase();" +
                             "const rect = candidate.getBoundingClientRect();" +
                             "if (!candidate.disabled && candidate.getClientRects().length > 0 && rect.width > 0 && rect.height > 0 && labels.some(item => label.includes(item))) { button = candidate; break; } } }" +
                             "if (!button) return null;" +
                             "const r = button.getBoundingClientRect();" +
                             "return { x: r.left + r.width / 2, y: r.top + r.height / 2, tag: button.tagName, text: (button.innerText || button.getAttribute('aria-label') || '').slice(0, 80) };" +
                             "})()";

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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "GoogleLogin",
                    "DispatchMouseClickNoButton",
                    "No submit button found for trusted click",
                    new { field = field.ToString() });
                return;
            }

            var x = value.GetProperty("x").GetDouble();
            var y = value.GetProperty("y").GetDouble();
            var tag = value.TryGetProperty("tag", out var tagProp) ? tagProp.GetString() : "";
            var text = value.TryGetProperty("text", out var textProp) ? textProp.GetString() : "";

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "GoogleLogin",
                "DispatchMouseClickTarget",
                "Trusted click target",
                new { field = field.ToString(), x, y, tag, text });

            // Dispatch real mouse events
            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseMoved",
                x = x,
                y = y,
                button = "none",
                buttons = 0
            }, cancellationToken, _sessionId);

            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mousePressed",
                x = x,
                y = y,
                button = "left",
                buttons = 1,
                clickCount = 1
            }, cancellationToken, _sessionId);

            await Task.Delay(50, cancellationToken);

            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseReleased",
                x = x,
                y = y,
                button = "left",
                buttons = 0,
                clickCount = 1
            }, cancellationToken, _sessionId);
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "GoogleLogin",
                "DispatchMouseClickFailed",
                "Trusted click failed",
                new { field = field.ToString(), error = ex.Message });
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

    private async Task LogPageStateForDebugAsync(GoogleLoginField field, CancellationToken cancellationToken)
    {
        try
        {
            const string diagnosticScript = @"
(function() {
    const result = { url: window.location.href, pathname: window.location.pathname, buttons: [] };

    // Check all button candidates
    const buttonSelectors = ['#identifierNext', '#passwordNext', '#totpNext', '[jsname=""LgbsSe""]',
                             '[jsname=""Njthtb""]', '[data-primary-action-label]', 'button[type=""submit""]'];
    buttonSelectors.forEach(sel => {
        const el = document.querySelector(sel);
        if (el) {
            const rect = el.getBoundingClientRect();
            result.buttons.push({
                selector: sel,
                text: el.innerText || el.textContent || '',
                disabled: el.disabled,
                visible: el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0,
                width: rect.width,
                height: rect.height
            });
        }
    });

    // Check text-based buttons
    const labels = ['next', 'tiếp', 'continue', 'sign in', 'đăng nhập', 'submit'];
    const candidates = Array.from(document.querySelectorAll('[role=""button""], button'));
    candidates.forEach(btn => {
        const label = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        if (labels.some(item => label.includes(item))) {
            const rect = btn.getBoundingClientRect();
            result.buttons.push({
                selector: 'text-match',
                text: btn.innerText || btn.textContent || '',
                ariaLabel: btn.getAttribute('aria-label'),
                disabled: btn.disabled,
                visible: btn.getClientRects().length > 0 && rect.width > 0 && rect.height > 0,
                width: rect.width,
                height: rect.height
            });
        }
    });

    return result;
})()";

            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = diagnosticScript,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (!result.TryGetProperty("exceptionDetails", out _))
            {
                var value = result.GetProperty("result").GetProperty("value");
                // DebugConsole.WriteLine($"[Submit] DIAGNOSTIC for {field}:");
                // DebugConsole.WriteLine($"  URL: {value.GetProperty("url").GetString()}");
                // DebugConsole.WriteLine($"  Pathname: {value.GetProperty("pathname").GetString()}");

                var buttons = value.GetProperty("buttons");
                // DebugConsole.WriteLine($"  Found {buttons.GetArrayLength()} button candidates:");

                foreach (var btn in buttons.EnumerateArray())
                {
                    var selector = btn.GetProperty("selector").GetString();
                    var text = btn.TryGetProperty("text", out var t) ? t.GetString() : "";
                    var disabled = btn.GetProperty("disabled").GetBoolean();
                    var visible = btn.GetProperty("visible").GetBoolean();
                    var width = btn.GetProperty("width").GetDouble();
                    var height = btn.GetProperty("height").GetDouble();

                    // DebugConsole.WriteLine($"    [{selector}] text=\"{text}\" disabled={disabled} visible={visible} size={width}x{height}");
                }
            }
        }
        catch (Exception)
        {
            // DebugConsole.WriteLine($"[Submit] Failed to log page state: {ex.Message}");
        }
    }

    private async Task<bool> TryClickConfirmIdentifierContinueAsync(CancellationToken cancellationToken)
    {
        try
        {
            const string clickScript = @"
(function() {
    // Find Continue/Next button on confirmidentifier page
    const buttonSelectors = [
        '#identifierNext',
        'button[type=""submit""]',
        '[jsname=""LgbsSe""]',
        '[jsname=""Njthtb""]',
        '[data-primary-action-label]'
    ];

    // Try by selector first
    for (const selector of buttonSelectors) {
        const btn = document.querySelector(selector);
        if (btn && !btn.disabled) {
            const rect = btn.getBoundingClientRect();
            if (btn.getClientRects().length > 0 && rect.width > 0 && rect.height > 0) {
                // Return button position for real mouse click
                return {
                    clicked: true,
                    method: 'selector',
                    selector: selector,
                    x: rect.left + rect.width / 2,
                    y: rect.top + rect.height / 2
                };
            }
        }
    }

    // Try by text matching
    const labels = ['continue', 'next', 'tiếp theo', 'tiếp'];
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]'));
    for (const btn of buttons) {
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        if (labels.some(label => text.includes(label)) && !btn.disabled) {
            const rect = btn.getBoundingClientRect();
            if (btn.getClientRects().length > 0 && rect.width > 0 && rect.height > 0) {
                // Return button position for real mouse click
                return {
                    clicked: true,
                    method: 'text',
                    text: btn.innerText,
                    x: rect.left + rect.width / 2,
                    y: rect.top + rect.height / 2
                };
            }
        }
    }

    return { clicked: false };
})()";

            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = clickScript,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("exceptionDetails", out var ex))
            {
                // DebugConsole.WriteLine($"[ConfirmIdentifier] Click failed: {GetCdpExceptionDescription(ex)}");
                return false;
            }

            var value = result.GetProperty("result").GetProperty("value");
            var clicked = value.TryGetProperty("clicked", out var c) && c.GetBoolean();

            if (clicked)
            {
                var method = value.TryGetProperty("method", out var m) ? m.GetString() : "unknown";
                var x = value.GetProperty("x").GetDouble();
                var y = value.GetProperty("y").GetDouble();

                // DebugConsole.WriteLine($"[ConfirmIdentifier] Found Continue button via {method}, dispatching real mouse click at ({x}, {y})");

                // Dispatch REAL mouse events instead of just .click()
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseMoved",
                    x = x,
                    y = y
                }, cancellationToken, _sessionId);

                await Task.Delay(100, cancellationToken);

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

                // DebugConsole.WriteLine($"[ConfirmIdentifier] Real mouse click dispatched successfully");
            }
            else
            {
                // DebugConsole.WriteLine("[ConfirmIdentifier] Could not find Continue button");
            }

            return clicked;
        }
        catch (Exception)
        {
            // DebugConsole.WriteLine($"[ConfirmIdentifier] Exception: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryClickRecaptchaCheckboxAsync(CancellationToken cancellationToken)
    {
        try
        {
            const string clickScript = @"
(function() {
    // Find reCAPTCHA checkbox iframe and click it
    // reCAPTCHA v2 structure: main page has iframe with checkbox

    // Method 1: Find iframe by title/src containing 'recaptcha'
    const iframes = Array.from(document.querySelectorAll('iframe'));
    const recaptchaIframe = iframes.find(iframe => {
        const src = iframe.src || '';
        const title = iframe.title || '';
        return src.includes('recaptcha') || title.toLowerCase().includes('recaptcha');
    });

    if (!recaptchaIframe) {
        return { clicked: false, reason: 'iframe_not_found' };
    }

    // Get iframe position for clicking
    const rect = recaptchaIframe.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) {
        return { clicked: false, reason: 'iframe_invisible' };
    }

    // Click center of reCAPTCHA iframe (checkbox is usually at center-left)
    // Adjust to click slightly left of center where checkbox typically is
    return {
        clicked: true,
        x: rect.left + 50,  // Checkbox is ~50px from left edge
        y: rect.top + rect.height / 2
    };
})()";

            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = clickScript,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("exceptionDetails", out var ex))
            {
                // DebugConsole.WriteLine($"[Recaptcha] Click failed: {GetCdpExceptionDescription(ex)}");
                return false;
            }

            var value = result.GetProperty("result").GetProperty("value");
            var clicked = value.TryGetProperty("clicked", out var c) && c.GetBoolean();

            if (clicked)
            {
                var x = value.GetProperty("x").GetDouble();
                var y = value.GetProperty("y").GetDouble();

                // DebugConsole.WriteLine($"[Recaptcha] Found reCAPTCHA iframe, dispatching real mouse click at ({x}, {y})");

                // Dispatch REAL mouse events
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseMoved",
                    x = x,
                    y = y
                }, cancellationToken, _sessionId);

                await Task.Delay(100, cancellationToken);

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

                // DebugConsole.WriteLine($"[Recaptcha] Real mouse click dispatched successfully");
            }
            else
            {
                var reason = value.TryGetProperty("reason", out var r) ? r.GetString() : "unknown";
                // DebugConsole.WriteLine($"[Recaptcha] Could not click checkbox: {reason}");
            }

            return clicked;
        }
        catch (Exception)
        {
            // DebugConsole.WriteLine($"[Recaptcha] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryClickConsentContinueAsync(CancellationToken cancellationToken)
    {
        // Must be on accounts.google.com consent/OAuth page
        try
        {
            var checkPage = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = "(function(){const h=window.location.hostname.toLowerCase();const p=window.location.pathname.toLowerCase();const t=(document.body&&document.body.innerText||'').toLowerCase();const isGoogle=h==='accounts.google.com'||h==='myaccount.google.com';const isConsent=p.includes('/signin/oauth')||p.includes('/o/oauth2')||t.includes('continue to')||t.includes('choose what to allow')||t.includes('cho phép')||t.includes('đồng ý');return {ok:isGoogle&&isConsent,host:h,path:p};})()",
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);
            if (checkPage.TryGetProperty("result", out var cp) && cp.TryGetProperty("value", out var cv))
            {
                if (!(cv.TryGetProperty("ok", out var okp) && okp.ValueKind == JsonValueKind.True))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        // Wait up to 5s for loading spinner to disappear (same pattern as TrySelectAuthenticatorMethodAsync)
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
                    break;
                }
            }
            catch
            {
                // Intentionally swallow: spinner check is optional
            }

            await Task.Delay(500, cancellationToken);
        }

        try
        {
            const string script = @"
(function() {
    const isVisible = element => {
        const rect = element.getBoundingClientRect();
        return element.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    const keywords = ['tiếp tục', 'continue', 'allow', 'cho phép', 'agree', 'authorize', 'accept'];
    const avoid = ['try another way', 'cancel', 'thử cách khác', 'hủy'];

    const candidates = Array.from(document.querySelectorAll('button, [role=""button""], a, [role=""link""]')).filter(el => {
        if (!isVisible(el)) return false;
        const text = ((el.innerText || '') + ' ' + (el.getAttribute('aria-label') || '')).toLowerCase();
        if (avoid.some(a => text.includes(a))) return false;
        return keywords.some(k => text.includes(k));
    });

    if (candidates.length === 0) {
        return { found: false };
    }

    // Prefer jsname LgbsSe or role button
    let best = candidates[0];
    let bestScore = -1;
    for (const el of candidates) {
        let score = 0;
        if (el.getAttribute('jsname') === 'LgbsSe') score += 2;
        if (el.getAttribute('role') === 'button' || el.tagName.toLowerCase() === 'button') score += 1;
        if (score > bestScore) { bestScore = score; best = el; }
    }

    const rect = best.getBoundingClientRect();
    return {
        found: true,
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

            if (!result.TryGetProperty("result", out var remoteObject)
                || !remoteObject.TryGetProperty("value", out var value))
            {
                return false;
            }

            if (!(value.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.True)
                || !value.TryGetProperty("centerX", out var centerX)
                || !value.TryGetProperty("centerY", out var centerY))
            {
                return false;
            }

            var x = centerX.GetDouble();
            var y = centerY.GetDouble();

            // mouseMoved before
            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseMoved",
                x = x,
                y = y,
                button = "none",
                buttons = 0
            }, cancellationToken, _sessionId);

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

            return true;
        }
        catch
        {
            return false;
        }
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
