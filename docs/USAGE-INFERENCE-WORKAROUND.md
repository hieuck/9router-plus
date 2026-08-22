# Usage Quota Inference - Workaround Implementation

**Date:** 2026-08-22  
**Status:** ✅ Implemented and Tested  
**Version:** RouterPlus 0.3.0+

## Overview

RouterPlus now **automatically infers usage quota information** from error messages when 9Router backend doesn't provide explicit usage data. This is a workaround solution that provides immediate value while waiting for backend implementation.

## How It Works

### 1. Detection

When `RouterApiClient.ParseConnection()` receives a connection:

1. First, it tries to parse `usageCount`, `limitCount`, `usageResetAt` from API response (normal path)
2. If these fields are missing, it checks error messages
3. If error indicates a quota/limit issue (429, 402), it infers usage data

### 2. Inference Logic

`UsageInferenceService` analyzes error codes and messages:

**Error Codes:**
- `429` - Too Many Requests (rate limit)
- `402` - Payment Required (credits exhausted)

**Error Patterns:**
- "usage limit has been reached"
- "reached the limit"
- "reached your weekly/monthly limit"
- "credits exhausted"
- "requires more credits"

### 3. Provider-Specific Handling

**Codex:**
```
Error: "[429]: The usage limit has been reached"
→ Inferred: 100/100 (100%), reset on 1st of next month
```

**Kiro:**
```
Error: "[402]: {\"message\":\"You have reached the limit.\",\"reason\":\"MONTHLY_REQUEST_COUNT\"}"
→ Inferred: 100/100 (100%), reset on 1st of next month
```

**OpenRouter:**
```
Error: "[402]: This request requires more credits..."
→ Inferred: 10000/10000 (100%), reset on 1st of next month

Error: "You requested 5.50 credits but only have 2.30 remaining"
→ Parsed: (550 - 230) / (550 + 230 + buffer) cents, reset on 1st of next month
```

**Ollama:**
```
Error: "[429]: you have reached your weekly usage limit"
→ Inferred: 100/100 (100%), reset next Monday

Error: "[429]: you have reached your session usage limit"
→ Inferred: 100/100 (100%), reset tomorrow
```

**Kimchi:**
```
Error: "[402]: the provider has exhausted its credits"
→ Inferred: 100/100 (100%), reset on 1st of next month
```

## What Users See

### Before (No Usage Data)
```
Sidebar:
🟢 CX  (just status, no usage info)

Provider Card:
Codex
Status: Unavailable
```

### After (With Inference)
```
Sidebar:
🔴 CX 100/100  (red badge showing at limit)

Provider Card:
Codex
[██████████] 100.0% đã sử dụng
100 / 100
Reset sau 9 ngày
⚠ Đã vượt hạn mức
```

## Limitations

### ⚠️ Known Limitations

1. **Only shows usage when limit is reached**
   - Cannot infer 50/100 (50%) from errors
   - Only knows 100/100 (100%) when error occurs

2. **Estimated values**
   - Usage shows "100/100" as placeholder
   - Real values might be "8500/10000"
   - Percentage is accurate (100%), absolute numbers are estimated

3. **Reset time may be approximate**
   - Monthly reset: calculated as 1st of next month
   - Weekly reset: calculated as next Monday
   - Actual provider reset time may differ

4. **No data for healthy connections**
   - Connections with `testStatus: "active"` won't show usage
   - Need backend API for sub-limit usage tracking

## Code Changes

### New Files

**`src/RouterPlus.Core/Providers/UsageInferenceService.cs`**
- Static service to infer usage from errors
- Provider-specific parsing logic
- Reset time calculations

**`tests/RouterPlus.Core.Tests/UsageInferenceServiceTests.cs`**
- 10 unit tests covering all providers
- Edge cases and error patterns

**`tests/RouterPlus.Core.Tests/UsageInferenceIntegrationTests.cs`**
- 7 integration tests with real 9Router errors
- Validates end-to-end parsing

### Modified Files

**`src/RouterPlus.Infrastructure/Router/RouterApiClient.cs`**
```csharp
// After parsing usageCount, limitCount, usageResetAt from JSON
if (!usageCount.HasValue && !limitCount.HasValue)
{
    var inferred = UsageInferenceService.InferUsageFromError(
        provider, errorCode, lastError, lastErrorAt);
    
    if (inferred is not null)
    {
        usageCount = inferred.UsageCount;
        limitCount = inferred.LimitCount;
        usageResetAt = inferred.UsageResetAt;
    }
}
```

## Testing

### Unit Tests (10 total)
```bash
.\.dotnet\dotnet.exe test --filter "FullyQualifiedName~UsageInferenceServiceTests"
# Result: Passed: 10, Failed: 0
```

**Covers:**
- Null/empty errors → no inference
- Non-limit errors (400) → no inference
- Codex 429 errors → infers monthly limit
- Kiro 402 errors → infers monthly limit
- OpenRouter 402 errors → infers credits
- OpenRouter with parsed amounts
- Ollama weekly/session limits
- Kimchi credits exhausted
- Reset time calculations

### Integration Tests (7 total)
```bash
.\.dotnet\dotnet.exe test --filter "FullyQualifiedName~UsageInferenceIntegrationTests"
# Result: Passed: 7, Failed: 0
```

**Tests real 9Router errors:**
- Codex: `[429]: The usage limit has been reached`
- Kiro: `[402]: MONTHLY_REQUEST_COUNT`
- Ollama: `[429]: session usage limit`
- OpenRouter: `[402]: requires more credits`
- Kimchi: `[402]: credits exhausted`
- Active connections → no inference
- Non-limit errors → no inference

## When Backend Implements Proper API

When 9Router backend adds `usageCount`, `limitCount`, `usageResetAt` to `/api/providers`:

1. **RouterPlus automatically uses real data**
   - Inference service only runs when fields are missing
   - Real data takes precedence

2. **No breaking changes needed**
   - Existing code path already supports these fields
   - Inference is fallback only

3. **Users get better experience**
   - Accurate usage numbers (e.g., 8500/10000 instead of 100/100)
   - Real-time updates (not just when limit reached)
   - Precise reset times

## FAQ

**Q: Why not wait for backend implementation?**  
A: This provides immediate value. Users can see when limits are reached NOW, without waiting for backend changes.

**Q: Is this accurate?**  
A: Percentage is accurate (100% when limit reached). Absolute numbers are placeholders. Reset times are best estimates.

**Q: Does this work for all providers?**  
A: Yes, tested with real errors from Codex, Kiro, OpenRouter, Ollama, and Kimchi.

**Q: What if I'm at 80% usage?**  
A: Inference can't detect this. You'll only see usage when you hit the limit. Backend API needed for sub-limit tracking.

**Q: Can I disable this?**  
A: No need. When backend provides real data, inference is automatically skipped.

**Q: Does this send extra API requests?**  
A: No. It only analyzes existing error messages from normal sync operations.

## Related Documentation

- [Current Situation Analysis](CURRENT-SITUATION-ANALYSIS.md) - Why backend changes are needed
- [9Router Usage API Integration](9router-usage-api-integration.md) - How backend should implement proper API
- [Provider Usage API Examples](provider-usage-api-examples.md) - Code examples for backend
- [Usage Quota Summary](USAGE-QUOTA-SUMMARY.md) - Quick reference

## Future Improvements

When backend implements proper usage API:

1. **Remove inference for providers with real data**
   - Keep inference as fallback for providers without API
   
2. **Add debug logging**
   - Log when inference is used vs real data
   - Help diagnose backend integration issues

3. **Add user setting**
   - "Prefer estimated usage over no data" (default: on)
   - Let users disable inference if they prefer

4. **Add tooltip indicator**
   - Show "⚠ Estimated" badge when using inferred data
   - Make it clear data is approximate

---

**Implementation Status:** ✅ Complete  
**Tests:** ✅ 17/17 passing  
**Ready for:** Production use
