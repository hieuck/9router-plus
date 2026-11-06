using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Storage;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP adapter that implements IGoogleLoginBrowser for Google login automation.
/// </summary>
internal sealed partial class GoogleLoginCdpBrowser : IGoogleLoginBrowser
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
}
