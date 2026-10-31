$path = "E:\GitHub\9router-plus\src\RouterPlus.Infrastructure\Chrome\GoogleLoginStateMachine.cs"
$content = Get-Content $path -Raw
$old = @"
                // Consent handling after password submission
                if (!state.HasCompletionSignal && !state.Has2FAMethodPicker && !state.HasManualChallenge)
                {
                    var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                    if (consentClicked)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                        {
                            return GoogleLoginResult.Success();
                        }
                    }
                    else
                    {
                        await Task.Delay(1000, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasConsentScreen)
                        {
                            await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                            await Task.Delay(1500, totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                            {
                                return GoogleLoginResult.Success();
                            }
                        }
                    }
                }
"@
$new = @"
                // Consent handling after password submission
                if (!state.HasCompletionSignal && !state.Has2FAMethodPicker && !state.HasManualChallenge && !state.HasTotpField && !state.HasAccountChooser)
                {
                    var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                    if (consentClicked)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                        {
                            return GoogleLoginResult.Success();
                        }
                    }
                    else
                    {
                        await Task.Delay(1000, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasConsentScreen)
                        {
                            await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                            await Task.Delay(1500, totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                            {
                                return GoogleLoginResult.Success();
                            }
                        }
                    }
                }
"@
if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    Set-Content $path $content -NoNewline
    Write-Host "ok"
} else {
    Write-Host "not found"
}
