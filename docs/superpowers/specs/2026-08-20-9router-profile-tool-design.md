# 9Router Profile Tool Design

**Date:** 2026-08-20

## Goal

Build a Windows desktop assistant that discovers Chrome profiles, opens a selected profile, and guides the user through adding Codex, Kiro, OpenRouter, Ollama Cloud, and Kimchi connections to a local 9Router dashboard.

## Scope

The first release supports:

- Detecting `chrome.exe` and the Chrome User Data directory, with manual path overrides.
- Listing Chrome profiles from `Local State` and launching a selected profile by double-click.
- Configuring the 9Router dashboard base URL, defaulting to `http://localhost:20128`.
- Quick actions for `Add Codex`, `Add Kiro`, `Add OpenRouter`, `Add Ollama`, and `Add Kimchi`.
- Quick links to OpenRouter API keys, Ollama API keys, and Kimchi login.
- OAuth-assisted flows that open the correct dashboard/provider page and wait for the user to finish Google/OAuth authorization.
- API-key-assisted flows that accept a key in a protected input, append the connection at the end of the provider priority list, and save the connection under the selected Chrome profile name.
- Persisting a profile/provider mapping and storing API keys in Windows DPAPI-protected local storage.
- Redacting secrets from logs and status messages.

The first release does not automate password entry, CAPTCHA solving, account creation, or third-party consent without the user.

## Provider Workflows

### Codex

Open the selected Chrome profile, navigate to the Codex provider page, open the add connection flow, and wait for the user to choose or sign in to a Google account. Poll the local 9Router provider API until a new Codex connection appears, then associate it with the selected profile.

### Kiro

Open the Kiro provider page, start the add flow, choose AWS Builder ID, open the device-login URL, and wait for user authorization. After the connection appears, update its display name to the selected Chrome profile name and verify it through the provider API.

### OpenRouter and Ollama Cloud

Open the provider's external API-key page using the selected Chrome profile. The user completes login, consent, key creation, and copy. The user pastes the key into the tool's protected field. The tool validates the key, chooses `max(existing priority) + 1`, creates the local 9Router connection, names it after the selected Chrome profile, and stores the key in the encrypted vault.

### Kimchi

Open the Kimchi provider page, start the OAuth flow, and wait for the user to complete Google authorization. Detect the new connection, associate it with the selected profile, and expose a quick link to `https://app.kimchi.dev/` for manual login or account preparation.

## Architecture

Use a small WPF shell over focused .NET services:

- `RouterPlus.Core`: provider definitions, profile models, priority calculation, workflow state, and validation rules.
- `RouterPlus.Infrastructure`: Chrome discovery/launching, 9Router HTTP client, DPAPI vault, and JSON persistence.
- `RouterPlus.App`: WPF views and view models for profile selection, quick actions, settings, and workflow status.
- `RouterPlus.Core.Tests`: deterministic tests for discovery parsing, provider routing, priority ordering, and secret-key mapping.

The local 9Router HTTP API is preferred for connection creation, rename, priority, and status polling. Browser automation is used only for navigation and user-led OAuth/device-login steps, keeping selectors and secret handling out of the core logic.

## Data and Security

- A stable profile key is derived from the Chrome User Data directory and profile directory, not only the display name.
- Non-secret settings and profile metadata are stored in `%LOCALAPPDATA%\\9RouterPlus\\settings.json`.
- API keys are stored under DPAPI protection in `%LOCALAPPDATA%\\9RouterPlus\\secrets.json`.
- Access tokens, API keys, callback URLs, and device codes are never written to logs.
- The dashboard URL is user-configurable and all HTTP calls require the same-origin local instance configured in settings.
- Adding a connection is idempotent by provider + profile key + normalized connection name; the tool must not silently create duplicates.

## Acceptance Criteria

1. The app discovers an installed Chrome executable and at least one Chrome profile, while allowing manual additions.
2. Double-clicking a profile launches Chrome with the requested profile directory.
3. Each provider button opens the correct dashboard page; the three quick-link buttons open the exact external URLs from the requirements.
4. API-key workflows compute the last priority, save the provider connection, and persist the encrypted profile/provider mapping.
5. OAuth workflows clearly pause for user authorization and resume when the local dashboard reports the new connection.
6. Unit tests cover the core decisions and a build/test command can be run from the repository using the local SDK.
