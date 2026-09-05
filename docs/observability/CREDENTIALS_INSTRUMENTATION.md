# Credentials Manager Observability Instrumentation

**Date:** 2026-09-05  
**Component:** CredentialsManagerViewModel  
**Purpose:** Track credential operations for security monitoring and performance optimization

## Overview

Added comprehensive observability instrumentation to `CredentialsManagerViewModel` to monitor:
- Vault unlock operations and success rates
- Credential save/update operations
- Google login attempts and outcomes
- Health check operations
- Batch login workflows

## Events Logged

### Vault Operations

#### VaultUnlockFailed
```json
{
  "level": "Error",
  "category": "CredentialsManager",
  "event": "VaultUnlockFailed",
  "message": "Failed to unlock vault",
  "context": {
    "error": "...",
    "error_type": "CryptographicException"
  }
}
```

### Credential Operations

#### SaveCredentialFailed
```json
{
  "level": "Error",
  "category": "CredentialsManager",
  "event": "SaveCredentialFailed",
  "message": "Failed to save credential",
  "context": {
    "profile": "Profile Default",
    "error": "..."
  }
}
```

#### GoogleLoginFailed
```json
{
  "level": "Error",
  "category": "CredentialsManager",
  "event": "GoogleLoginFailed",
  "message": "Google login failed with exception",
  "context": {
    "profile": "Profile Default",
    "error": "..."
  }
}
```

#### HealthCheckFailed
```json
{
  "level": "Error",
  "category": "CredentialsManager",
  "event": "HealthCheckFailed",
  "message": "Health check failed with exception",
  "context": {
    "profile": "Profile Default",
    "error": "..."
  }
}
```

#### BatchLoginStarted
```json
{
  "level": "Info",
  "category": "CredentialsManager",
  "event": "BatchLoginStarted",
  "message": "Starting batch login operation",
  "context": {
    "profile_count": 5
  }
}
```

## Metrics Tracked

### Counters

| Metric | Tags | Purpose |
|--------|------|---------|
| `vault.unlock.success` | - | Count successful vault unlocks |
| `vault.unlock.failed` | `error_type` | Count failed unlock attempts by error type |
| `credentials.save.success` | - | Count successful credential saves |
| `credentials.save.failed` | `reason` | Count failed saves (profile_not_found, exception) |
| `credentials.login.google` | `result` | Count login attempts by result category |
| `credentials.login.google.exception` | `error_type` | Count login exceptions by type |
| `credentials.health_check.completed` | `status` | Count health checks by final status |
| `credentials.health_check.exception` | `error_type` | Count health check exceptions |
| `credentials.batch_login.completed` | - | Count completed batch operations |
| `credentials.batch_login.cancelled` | - | Count cancelled batch operations |

### Gauges

| Metric | Purpose |
|--------|---------|
| `vault.profiles.configured` | Number of profiles with stored credentials |
| `credentials.batch_login.success_count` | Successful logins in last batch |
| `credentials.batch_login.fail_count` | Failed logins in last batch |

### Histograms (via TraceScope)

| Operation | Unit | Purpose |
|-----------|------|---------|
| `UnlockVault` | milliseconds | Time to unlock vault |
| `SaveCredential` | milliseconds | Time to save credential |
| `LoginGoogle` | milliseconds | Time for Google login |
| `CheckHealth` | milliseconds | Time for health check |
| `BatchLogin` | milliseconds | Total time for batch operation |

## TraceScope Checkpoints

### UnlockVault
- `OpeningExistingVault` - Opening existing vault file
- `CreatingNewVault` - Creating new vault (first-time setup)
- `RememberingKey` - Persisting encryption key to device

### SaveCredential
- `SavingToVault` - Writing to encrypted vault storage

## Use Cases

### Security Monitoring

**Failed unlock attempts:**
```bash
grep '"event":"VaultUnlockFailed"' events.jsonl | wc -l
```

**Identify profiles with frequent login failures:**
```bash
grep '"event":"GoogleLoginFailed"' events.jsonl | \
  jq -r '.context.profile' | \
  sort | uniq -c | sort -rn
```

### Performance Analysis

**Vault unlock timing:**
```bash
grep '"operation":"UnlockVault"' events.jsonl | \
  jq '.context.duration_ms' | \
  awk '{sum+=$1; count++} END {print "Avg:", sum/count, "ms"}'
```

**Health check distribution:**
```bash
grep 'credentials.health_check.completed' events.jsonl | \
  jq -r '.tags.status' | \
  sort | uniq -c
```

### Operational Insights

**Batch login success rate:**
```bash
# Get latest batch operation
grep 'batch_login' events.jsonl | tail -20 | \
  jq '{success: .name | select(. == "credentials.batch_login.success_count"), fail: .name | select(. == "credentials.batch_login.fail_count")}'
```

**Most common login failure categories:**
```bash
grep 'credentials.login.google' events.jsonl | \
  jq -r '.tags.result' | \
  sort | uniq -c | sort -rn
```

## Testing

All observability instrumentation is verified through existing unit tests:
- 28 observability-specific tests passing
- No breaking changes to existing API
- Events and metrics validated in integration tests

## Integration Points

### Existing Observability Infrastructure

- **ObservabilityHub**: Singleton event/metric collector
- **TraceScope**: Hierarchical operation timing with IDisposable pattern
- **JSON Lines**: Streaming log format for production analysis
- **Privacy Scrubbing**: Automatic redaction of sensitive data

### Related Components

- `GoogleAutoLoginViewModel` - Already instrumented for profile login workflows
- `MainViewModel` - Already instrumented for provider selection
- `WelcomeWizardWindow` - Already instrumented (fixed 72s black hole)

## Future Enhancements

1. **Provider login instrumentation**: Add similar instrumentation for Codex/Kiro/GitHub/OpenRouter logins
2. **Credential expiry detection**: Track when credentials fail due to password changes
3. **TOTP validation metrics**: Track 2FA success rates
4. **Vault performance**: Monitor vault encryption/decryption overhead
5. **User behavior patterns**: Analyze which profiles are most frequently accessed

## Implementation Notes

- All instrumentation is non-blocking and fire-and-forget
- TraceScope automatically captures operation timing
- Dictionary tags enable filtering and aggregation in production
- Error messages are sanitized (using `GetSafeVaultErrorMessage`)
- No sensitive data (passwords, TOTP secrets) logged to events
