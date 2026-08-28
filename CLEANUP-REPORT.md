# Repository Cleanup Report

**Date:** 2026-08-28  
**Repository:** 9router-plus  
**Total size:** ~18MB (excluding .git, .dotnet, bin, obj)

---

## Summary

**Files identified for cleanup:**
- 📦 **Total reclaimable space:** ~14.4 MB
- 📄 **Temporary files:** 51 files
- 🗂️ **Directories:** 2 directories (.bootstrap, .playwright-mcp)

---

## Cleanup Categories

### 🔴 High Priority - Development Artifacts

#### 1. Patch Files (14+ MB)
- **Root directory:**
  - `usage_fix.patch` (1.4K)
  - `token_db_priority.patch` (3.2K)
  
- **.bootstrap/ directory:**
  - 37 patch files (~14MB total)
  - Examples: `core-implementation.patch`, `infrastructure-chrome.patch`, `oauth-api.patch`
  
**Status:** Already in .gitignore  
**Recommendation:** ✅ **SAFE TO DELETE** - These are old development patches, already committed

#### 2. Build Logs
- `.bootstrap/infra-build.log` (193K)
- `.bootstrap/solution-build.log`

**Status:** Already in .gitignore  
**Recommendation:** ✅ **SAFE TO DELETE**

---

### 🟡 Medium Priority - Runtime Logs

#### 3. Application Logs (350+ KB)
- `stdout.log` (334K) ⚠️ **Largest file**
- `stderr.log` (8.5K)
- `app-debug.log` (3.7K)
- `app-output.log` (98 bytes)

**Status:** Already in .gitignore  
**Recommendation:** ✅ **SAFE TO DELETE** - Runtime logs, regenerated on next run

#### 4. Playwright Console Logs (31K)
- `.playwright-mcp/console-2026-08-27T*.log` (5 files)

**Status:** Directory in .gitignore  
**Recommendation:** ✅ **SAFE TO DELETE** - Old browser automation logs

---

### 🟢 Low Priority - Temporary Scripts

#### 5. CSX Debug Scripts (3.8K)
- `query_db.csx` (1.2K) - Database query script
- `test_debug.csx` (778 bytes) - Debug test script
- `verify_fix.csx` (1.8K) - Fix verification script

**Status:** NOT in .gitignore  
**Recommendation:** ⚠️ **REVIEW BEFORE DELETE** - May contain useful queries for future debugging

#### 6. Crash Dumps
- `bash.exe.stackdump` (1.2K)

**Status:** Already in .gitignore  
**Recommendation:** ✅ **SAFE TO DELETE**

---

### 📋 Untracked Files to Review

#### 7. Security Audit Report
- `SECURITY-AUDIT-REPORT.md` (4.6K)

**Status:** Untracked, not in .gitignore  
**Recommendation:** ⚠️ **COMMIT THIS** - Important security documentation

---

## Cleanup Commands

### Option 1: Safe Cleanup (Recommended)
Remove only logs and patch files that are definitely temporary:

```powershell
# Remove log files
Remove-Item -Force stdout.log, stderr.log, app-debug.log, app-output.log, bash.exe.stackdump

# Remove root patch files
Remove-Item -Force *.patch

# Clean .bootstrap directory
Remove-Item -Recurse -Force .bootstrap

# Clean .playwright-mcp logs
Remove-Item -Force .playwright-mcp\*.log

# Stage security audit report
git add SECURITY-AUDIT-REPORT.md
```

**Space saved:** ~14.4 MB  
**Preserves:** CSX scripts for future reference

---

### Option 2: Deep Cleanup
Includes Option 1 + removes CSX debug scripts:

```powershell
# Everything from Option 1, plus:
Remove-Item -Force *.csx
```

**Space saved:** ~14.4 MB + 3.8K

---

### Option 3: Nuclear Cleanup
Complete cleanup including bin/obj directories:

```powershell
# Everything from Option 2, plus:
Get-ChildItem -Recurse -Directory -Filter "bin" | Remove-Item -Recurse -Force
Get-ChildItem -Recurse -Directory -Filter "obj" | Remove-Item -Recurse -Force
dotnet clean
```

**Space saved:** Hundreds of MB  
**Note:** Requires rebuild (`dotnet build`)

---

## Gitignore Coverage

✅ **Well covered:**
- `*.log` - All log files
- `.bootstrap/` - Development directory
- `/.playwright-mcp` - Browser logs
- `*.patch` - Patch files
- `*.stackdump` - Crash dumps
- `bin/`, `obj/` - Build outputs

⚠️ **Not covered:**
- `*.csx` - Debug scripts not in .gitignore

**Recommendation:** Add to .gitignore:
```gitignore
# Temporary query/debug scripts
*.csx
```

---

## Modified Files to Commit

Before cleanup, commit pending changes:

```
M .gitignore
M Directory.Build.props
M docs/CURRENT-SITUATION-ANALYSIS.md
M docs/auto-login-vault-refactor-plan.md
M docs/debug-logging.md
M tests/RouterPlus.App.E2E/COVERAGE.md
M tests/RouterPlus.Core.Tests/UsageInferenceIntegrationTests.cs
?? SECURITY-AUDIT-REPORT.md
```

---

## Recommendations Priority

1. **Immediate:** Commit `SECURITY-AUDIT-REPORT.md`
2. **High:** Run Option 1 cleanup (safe, recovers 14+ MB)
3. **Medium:** Add `*.csx` to .gitignore
4. **Low:** Review CSX scripts before deletion
5. **Optional:** Run Option 3 if rebuilding is acceptable
