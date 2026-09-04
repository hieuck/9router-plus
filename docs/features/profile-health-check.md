# Profile Health Check

## Overview

Profile Health Check monitors Chrome profile integrity and alerts you to issues before they impact auto-login operations.

## What It Checks (Phase 1)

### Filesystem
- ✅ Profile directory exists and is accessible
- ✅ Chrome Local State file present
- ✅ Profile Preferences file present
- ℹ️ Secure Preferences file present (info only)

### Coming in Phase 2
- Vault integrity (vault files decryptable)
- Credentials configuration (Google accounts linked)
- Provider connections (active and healthy)

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
