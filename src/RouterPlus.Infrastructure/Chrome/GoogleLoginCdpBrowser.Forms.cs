using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Storage;

namespace RouterPlus.Infrastructure.Chrome;

internal sealed partial class GoogleLoginCdpBrowser
{

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
}