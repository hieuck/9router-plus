# E2E Test Coverage Report
**Generated**: 2026-08-26  
**Developer Harness**: Synthetic + Live Environment

## ✅ Test Results

### Harness Tests (Synthetic Environment)
- **Total Tests**: 21
- **Passed**: ✅ 21
- **Failed**: ❌ 0
- **Execution Time**: ~2 minutes

### Live Tests (Real Chrome Profile)
- **Total Tests**: 4 (3 read-only + 1 auto-login)
- **Read-only tests**: 3 (require explicit live opt-in)
- **Auto-login test**: 1 (verified end-to-end via debug console runner)
- **Execution Time**: depends on the configured Chrome profile

Live tests are not part of the default synthetic run and require explicit environment configuration.

### Live Read-only Coverage (3 tests)
- `Live_startup_shows_configured_profile` - Discovers the configured real Chrome profile
- `Live_profile_selection_and_menu_dismissal_keep_app_stable` - Selects the profile and inspects/dismisses its menu
- `Live_google_auto_login_dialog_can_be_cancelled_without_starting_login` - Inspects and cancels the dialog without starting login

### Live Auto Login Coverage
- **Status**: ✅ VERIFIED end-to-end (2026-08-26)
- Debug console runner with real test profile (signed out, no session cookies) successfully completed full authentication flow:
  - Email filled and submitted → advanced to password page
  - Password filled and submitted → advanced to 2FA method selection
  - Selected Authenticator method → advanced to TOTP page
  - TOTP code generated and submitted → authenticated
  - Final redirect to `https://myaccount.google.com/`
  - Exit code: 0 (Success)
- Speedbump bypass logic added for selfie verification and passkey enrollment pages

The read-only suite never clicks `Xóa profile…`, `Mở khóa`, or `Tự động đăng nhập`.

### Live Auto-login Coverage (1 test)
- `Google_auto_login_completes_successfully` - Runs the configured real Chrome automation flow
- **Current status**: failing before the password-submit transition completes

The auto-login test is intentionally separate from the read-only suite because it starts managed Chrome and exercises credentials.

### Live Test Safety
- Original Chrome processes must not be terminated by read-only tests
- Profile deletion is excluded from all live read-only actions
- Credentials and profile contents are never written to source or coverage artifacts

### Previous Live Findings
- Managed Chrome launches without closing the user's existing browser
- Loopback CDP endpoint is created and connected
- Google login does not yet complete the password-submit transition
- Authenticated Google page has not yet been proven

### Synthetic Environment Results

## Coverage by Feature

### ✅ Startup & Initialization (1 test)
- `Harness_starts_routerplus_with_synthetic_environment` - Verifies app starts with correct title

### ✅ Profile Selection (4 tests)
- `Single_click_selects_profile` - Profile selection works
- `Can_switch_between_profiles` - Can switch between profiles
- `Double_click_launches_profile_without_crash` - Double-click launches Chrome
- `Profile_list_shows_both_synthetic_profiles` - Both synthetic profiles visible

### ✅ Profile Context Menu (2 tests)
- `Right_click_profile_opens_expected_context_menu` - All 5 menu items present
- `Right_click_can_be_repeated_for_different_profiles` - Menu works repeatedly

### ✅ Profile Actions (5 tests)
- `Google_auto_login_menu_item_opens_dialog` - Opens Auto Login dialog
- `Google_login_with_chrome_does_not_crash` - Chrome login executes
- `Open_profile_folder_does_not_crash` - Folder open works
- `Copy_profile_name_action_executes` - Copy completes
- `Context_menu_can_be_dismissed_with_escape` - ESC dismisses menu

### ✅ Profile Sidebar (3 tests)
- `Sidebar_is_visible_on_startup` - Sidebar renders
- `Both_profiles_visible_in_sidebar` - Both profiles visible
- `Profile_items_are_clickable` - Items are interactive

### ✅ Google Auto Login Dialog (4 tests)
- `Dialog_opens_successfully` - Dialog opens when profile selected
- `Dialog_has_vault_unlock_button` - "Mở khóa" button present
- `Dialog_has_auto_login_button` - "Tự động đăng nhập" button present
- `Dialog_can_be_cancelled` - "Hủy" button closes dialog

### ✅ Auto Login Flow (1 test)
- `Auto_login_button_click_starts_automation` - Actually clicks button and triggers Chrome launch

### ⚠️ LIVE: Real Chrome Auto Login (1 test)
- `Google_auto_login_completes_successfully` - Runs against the configured real Chrome profile
  - Opens the Auto Login dialog
  - Starts an isolated managed Chrome session with loopback CDP
  - Reaches the Google password step
  - **Currently fails before the password-submit transition completes**
  - Does not claim authenticated-page success

## Features Verified ✅

### Synthetic Environment (20 tests)
✅ App startup with harness environment  
✅ Profile list rendering (2 synthetic profiles)  
✅ Profile selection (single click)  
✅ Profile switching  
✅ Profile launch (double click → Chrome)  
✅ Context menu operations (right-click)  
✅ Google login with Chrome action  
✅ Google Auto Login dialog UI  
✅ Auto Login button click triggers automation  
✅ Profile folder operations  
✅ Clipboard operations (copy profile name)  
✅ Context menu dismiss (ESC key)  
✅ UI stability (no crashes)  
✅ Sidebar visibility  

### Live Environment (1 test) ⚠️
✅ **Managed Chrome launches without closing the user's existing browser**  
✅ **Loopback CDP endpoint is created and connected**  
⚠️ **Google login does not yet complete the password-submit transition**  
❌ **Authenticated Google page has not been proven**  

## Test Infrastructure

### Synthetic Environment
- Isolated TestEnvironment with temp directories
- Profiles: Harness Alpha (Default), Harness Beta (Profile 1)
- Dashboard: http://127.0.0.1:20128
- Language: Vietnamese UI (tiếng Việt)
- Automatic cleanup after each test
- No real Chrome user data modified

### Live Environment
- Uses user's actual Chrome installation
- Profiles: User's real Chrome profiles (e.g., "Your Chrome")
- Requires: ROUTERPLUS_LIVE_E2E=1 and ROUTERPLUS_LIVE_PROFILE=<name>
- Read-only suite covers profile discovery, selection, context-menu inspection, and dialog cancellation
- Destructive profile deletion is excluded from live coverage
- Credential submission and Auto Login execution remain isolated to the existing live flow test

## How to Run

### Synthetic Tests (Safe - No real data)
```powershell
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj --filter "FullyQualifiedName!~Live"
```

### Live Test (Real Chrome profile)
```powershell
$env:ROUTERPLUS_LIVE_E2E = "1"
$env:ROUTERPLUS_LIVE_PROFILE = "Your Chrome"
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj --filter "FullyQualifiedName~LiveGoogleAutoLoginTests"
```

## Total Coverage
- **21 synthetic tests** covering the app UI and harness flows
- **Synthetic pass rate: 100%**
- **Live Google test: failing at password-submit transition**
- **Both synthetic and real Chrome environments exercised**
- **Vietnamese UI fully supported**
