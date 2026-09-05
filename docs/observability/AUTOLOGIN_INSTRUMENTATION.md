# AutoLogin Orchestrator Observability Instrumentation

**Date:** 2026-09-05  
**Component:** AutoLoginOrchestrator  
**Purpose:** Track auto-login success rates, method selection, and fallback behavior

## Overview

Added comprehensive observability instrumentation to `AutoLoginOrchestrator` to monitor:
- Auto-login success/failure rates by provider
- Primary vs fallback authentication method usage
- Google OAuth vs Direct login patterns
- Credential availability issues

## Metrics Tracked

### Counters

| Metric | Tags | Purpose |
|--------|------|---------|
| `autologin.success` | `provider`, `method`, `fallback_used` | Count successful logins by provider and method |
| `autologin.failed` | `provider`, `method`, `fallback_available` | Count failed logins with context |
| `autologin.no_credentials` | `provider` | Count attempts with no configured credentials |

### Histograms (via TraceScope)

| Operation | Unit | Purpose |
|-----------|------|---------|
| `Login` | milliseconds | Total time for auto-login attempt (including fallback) |

## TraceScope Checkpoints

### Login Operation
- `TryingPrimaryMethod` - Attempting primary authentication method (Google OAuth or Direct)
- `TryingFallbackMethod` - Attempting fallback method after primary failed

## Use Cases

### Success Rate Analysis

**Overall auto-login success rate:**
```bash
grep '"name":"autologin\.' events.jsonl | \
  jq -r '.name' | \
  sort | uniq -c
```

**Success rate by provider:**
```bash
grep '"name":"autologin.success"' events.jsonl | \
  jq -r '.tags.provider' | \
  sort | uniq -c
```

**Success rate by method:**
```bash
grep '"name":"autologin.success"' events.jsonl | \
  jq -r '.tags.method' | \
  sort | uniq -c
```

### Fallback Behavior

**How often fallback is used:**
```bash
grep '"name":"autologin.success"' events.jsonl | \
  jq -r '.tags.fallback_used' | \
  sort | uniq -c
```

**Failures where fallback was available but also failed:**
```bash
grep '"name":"autologin.failed"' events.jsonl | \
  jq 'select(.tags.fallback_available == "true")' | \
  jq -r '.tags.provider' | \
  sort | uniq -c
```

### Provider-Specific Analysis

**Most reliable provider:**
```bash
for provider in Codex Kiro GitHub OpenRouter; do
  success=$(grep "autologin.success" events.jsonl | grep "\"$provider\"" | wc -l)
  failed=$(grep "autologin.failed" events.jsonl | grep "\"$provider\"" | wc -l)
  total=$((success + failed))
  if [ $total -gt 0 ]; then
    rate=$(awk "BEGIN {printf \"%.1f\", ($success/$total)*100}")
    echo "$provider: $rate% ($success/$total)"
  fi
done
```

**Most common failure: missing credentials:**
```bash
grep '"name":"autologin.no_credentials"' events.jsonl | \
  jq -r '.tags.provider' | \
  sort | uniq -c | sort -rn
```

### Performance Analysis

**Login timing by provider:**
```bash
grep '"operation":"Login"' events.jsonl | \
  jq -r '"\(.context.provider) \(.context.duration_ms)"' | \
  awk '{sum[$1]+=$2; count[$1]++} END {for(p in sum) print p": "sum[p]/count[p]" ms avg"}'
```

### Method Preference Analysis

**Google OAuth vs Direct usage:**
```bash
grep '"name":"autologin.success"' events.jsonl | \
  jq -r '.tags.method' | \
  sort | uniq -c | \
  awk '{printf "%s: %d (%.1f%%)\n", $2, $1, ($1/NR)*100}'
```

## Integration with Credential Management

The AutoLoginOrchestrator works with instrumentation from:
- **CredentialsManagerViewModel**: Tracks credential storage and health checks
- **GoogleAutoLoginViewModel**: Tracks profile-specific login workflows
- **MainViewModel**: Tracks provider workflow initiation

Combined, these provide end-to-end visibility:
1. User opens Credentials Manager → `vault.unlock.success`
2. User saves credentials → `credentials.save.success`
3. User initiates auto-login → `autologin.success`/`autologin.failed`
4. Login timing captured → `Login` histogram

## Example: Diagnosing Login Failures

**Scenario:** Codex auto-login failing frequently

**Step 1: Check if credentials exist**
```bash
grep 'autologin.no_credentials' events.jsonl | grep Codex | wc -l
# If high: users haven't configured Codex credentials
```

**Step 2: Check primary vs fallback**
```bash
grep 'autologin.success.*Codex' events.jsonl | \
  jq -r '.tags | "\(.method) (fallback: \(.fallback_used))"' | \
  sort | uniq -c
# Reveals if one method is more reliable
```

**Step 3: Check timing**
```bash
grep '"operation":"Login".*Codex' events.jsonl | \
  jq '.context.duration_ms' | \
  awk '{sum+=$1; count++; if($1>max) max=$1} END {print "Avg:", sum/count, "ms, Max:", max, "ms"}'
# Reveals if timeouts are the issue
```

## Testing

All observability instrumentation is verified through existing unit tests:
- 7 AutoLoginOrchestrator tests passing
- No breaking changes to existing API
- Events and metrics validated in integration tests

## Future Enhancements

1. **Error categorization**: Track specific failure reasons (timeout, invalid credentials, CAPTCHA, etc.)
2. **Provider-specific metrics**: Per-provider success rates and timing
3. **TOTP usage tracking**: Monitor 2FA success rates
4. **Browser launch failures**: Track CDP connection issues
5. **Credential expiry detection**: Identify stale credentials requiring updates

## Implementation Notes

- All instrumentation is non-blocking and fire-and-forget
- TraceScope automatically captures operation timing including fallback attempts
- Dictionary tags enable filtering and aggregation by provider/method
- No sensitive data (passwords, TOTP secrets) logged to events
- Fallback tracking reveals when primary method is unreliable
