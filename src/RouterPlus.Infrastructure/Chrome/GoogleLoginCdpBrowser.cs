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
        ObjectDisposedException.ThrowIf(_disposed, this);

        await ValidateTargetAsync(cancellationToken);

        const string script = @"
(function(args) {
    const pageUrl = window.location.href;
    const hasEmailField = !!document.querySelector('input[type=""email""]');
    const hasPasswordField = !!document.querySelector('input[type=""password""]');
    const hasTotpField = !!document.querySelector('input[name=""totpPin""]');
    const hasCompletionSignal = window.location.hostname === 'myaccount.google.com' ||
                                 window.location.hostname === 'www.google.com' ||
                                 (window.location.hostname === 'accounts.google.com' &&
                                  document.querySelector('[data-profile-identifier], [aria-label*=""signed in""], [aria-label*=""Account""]'));
    const hasManualChallenge = !!document.querySelector('[data-challenge-id], .captcha, #captcha');

    return {
        pageUrl: pageUrl,
        hasEmailField: hasEmailField,
        hasPasswordField: hasPasswordField,
        hasTotpField: hasTotpField,
        hasCompletionSignal: hasCompletionSignal,
        hasManualChallenge: hasManualChallenge
    };
})({})
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        try
        {
            var value = result.GetProperty("result").GetProperty("value");

            var pageUrl = new Uri(value.GetProperty("pageUrl").GetString()!);
            var hasEmailField = value.GetProperty("hasEmailField").GetBoolean();
            var hasPasswordField = value.GetProperty("hasPasswordField").GetBoolean();
            var hasTotpField = value.GetProperty("hasTotpField").GetBoolean();
            var hasCompletionSignal = value.GetProperty("hasCompletionSignal").GetBoolean();
            var hasManualChallenge = value.GetProperty("hasManualChallenge").GetBoolean();

            return new GoogleLoginPageState(
                pageUrl,
                hasEmailField,
                hasPasswordField,
                hasTotpField,
                hasCompletionSignal,
                hasManualChallenge);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException("Failed to read page state.", ex);
        }
    }

    public async Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        await ValidateTargetAsync(cancellationToken);

        var selector = field switch
        {
            GoogleLoginField.Email => "input[type=\"email\"]",
            GoogleLoginField.Password => "input[type=\"password\"]",
            GoogleLoginField.Totp => "input[name=\"totpPin\"]",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        // Focus the input field using Runtime.callFunctionOn with JSON-encoded arguments
        const string focusFunction = @"
function(selector) {
    const element = document.querySelector(selector);
    if (!element) {
        throw new Error('Field not found');
    }
    element.focus();
    element.click();
    return true;
}";

        var callResult = await _client.CallAsync("Runtime.callFunctionOn", new
        {
            functionDeclaration = focusFunction,
            arguments = new[] { new { value = selector } },
            returnByValue = true
        }, cancellationToken, _sessionId);

        if (callResult.TryGetProperty("exceptionDetails", out _))
        {
            throw new InvalidOperationException($"Failed to focus {field} field.");
        }

        // Clear existing content with Ctrl+A
        await _client.CallAsync("Input.dispatchKeyEvent", new
        {
            type = "keyDown",
            modifiers = 2, // Ctrl
            key = "a"
        }, cancellationToken, _sessionId);

        await _client.CallAsync("Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            modifiers = 2,
            key = "a"
        }, cancellationToken, _sessionId);

        // Insert text using Input.insertText (does not use clipboard)
        await _client.CallAsync("Input.insertText", new { text = value }, cancellationToken, _sessionId);
    }

    public async Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await ValidateTargetAsync(cancellationToken);

        var selector = field switch
        {
            GoogleLoginField.Email => "input[type=\"email\"]",
            GoogleLoginField.Password => "input[type=\"password\"]",
            GoogleLoginField.Totp => "input[name=\"totpPin\"]",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        // Google uses step-specific div buttons rather than submit buttons.
        // Keep the function static and pass only the field selector as data.
        const string submitFunction = @"
function(selector) {
    const element = document.querySelector(selector);
    if (!element) {
        throw new Error('Field not found');
    }

    const nextButtonId = selector.includes('email')
        ? '#identifierNext'
        : selector.includes('password')
            ? '#passwordNext'
            : '#totpNext';
    const button = document.querySelector(nextButtonId) ||
                   document.querySelector('[role=""button""][data-primary-action-label]') ||
                   document.querySelector('button[type=""submit""]');
    const form = element.closest('form');

    if (button) {
        button.click();
    } else if (form) {
        form.requestSubmit ? form.requestSubmit() : form.submit();
    } else {
        throw new Error('Submit button not found');
    }

    return true;
}";

        var result = await _client.CallAsync("Runtime.callFunctionOn", new
        {
            functionDeclaration = submitFunction,
            arguments = new[] { new { value = selector } },
            returnByValue = true
        }, cancellationToken, _sessionId);

        if (result.TryGetProperty("exceptionDetails", out _))
        {
            throw new InvalidOperationException($"Failed to submit {field} field.");
        }

        // Wait for navigation to complete
        await Task.Delay(1000, cancellationToken);
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
                    // Allow accounts.google.com, myaccount.google.com, and www.google.com
                    if (uri.Host != "accounts.google.com" &&
                        uri.Host != "myaccount.google.com" &&
                        uri.Host != "www.google.com")
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
