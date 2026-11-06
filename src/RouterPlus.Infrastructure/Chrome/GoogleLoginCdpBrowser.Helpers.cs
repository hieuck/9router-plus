using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Storage;

namespace RouterPlus.Infrastructure.Chrome;

internal sealed partial class GoogleLoginCdpBrowser
{

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