# Profile Health Check

## Overview

Profile Health Check monitors Chrome profile integrity and alerts you to issues before they impact auto-login operations.

## What It Checks

### Filesystem
- ✅ Profile directory exists and is accessible
- ✅ Chrome Local State file present
- ✅ Profile Preferences file present
- ℹ️ Secure Preferences file present (info only)

### Credentials
- ⚠️ Google account linked to profile
- ⚠️ Credentials saved in vault

### Coming Later
- Vault integrity (vault files decryptable)
- Provider connections (active and healthy)

## Usage

### Viewing Health Status

Health status is displayed directly in the profile list for each profile:

- **Health Icon**: A visual indicator (✓/⚠/✗/?) appears next to each profile name
- **Status Text**: Brief description of the health state (e.g., "Healthy", "Warning", "Error")
- **Cached Results**: Status reflects cached results (refreshed every 5 minutes)

### Health Icons

| Icon | Meaning | Description |
|------|---------|-------------|
| ✓ | Healthy | All checks passed, profile ready to use |
| ⚠ | Warning | Minor issues detected, may need attention |
| ✗ | Error | Critical issues, profile likely unusable |
| ? | Unknown | Health check not yet performed |

### Manual Health Check

To manually check a specific profile:

1. Right-click the profile in the profile list
2. Select **"Check Profile Health"** from the context menu
3. The health status will refresh immediately (bypassing cache)

Manual checks are useful when:
- You've just fixed an issue and want to verify the profile is healthy
- You want to force a fresh check before an important operation
- The cached status seems outdated

## Performance

- First check: <100ms per profile
- Cached checks: <50ms per profile
- Cache expires after 5 minutes
- Manual "Check Profile Health" bypasses cache

## Understanding Results

**Healthy (✓)**
- All checks passed
- Profile is ready to use

**Warning (⚠)**
- Minor issues detected (e.g., preferences file missing)
- Profile may still work but needs attention
- Common causes:
  - Profile created but never used
  - Chrome hasn't been launched yet

**Error (✗)**
- Critical issues detected
- Profile likely unusable until resolved
- Common causes:
  - Profile directory deleted externally
  - File permission problems

## Troubleshooting

**"Profile directory not found"**
- Profile was deleted outside the app
- Recommendation: Remove profile from catalog

**"Cannot access profile directory"**
- File permission problem
- Recommendation: Check Windows file permissions

**"Chrome Local State file missing"**
- Chrome hasn't been launched yet
- Recommendation: Launch Chrome once

**"Profile Preferences file missing"**
- Profile created but never used
- Recommendation: Open Chrome with this profile

**"No Google account linked to this profile"**
- Profile has not been logged into Google
- Or credentials were not saved to vault
- Recommendation: Log in to Google in this profile and save credentials
