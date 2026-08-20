# Provider Dashboard Buttons Design

**Date:** 2026-08-20

## Goal

Add a per-provider button that opens the matching 9Router dashboard page in the currently selected Chrome profile, and make all provider card action buttons visually consistent.

## Decisions

- Build each provider dashboard URL from the configured DashboardBaseUrl and the provider catalog's existing DashboardPath.
- Reuse the existing LaunchUrlAsync flow so the configured Chrome executable, user-data directory, and selected Chrome profile are preserved.
- Add an OpenProviderDashboard command to the main view model and bind it from every provider card.
- Use a shared left-aligned WrapPanel action layout with Dashboard first; four API-key actions wrap as three buttons on the first row and one on the second.
- Keep the existing provider workflow and API-key behavior unchanged.

## Routes

- Codex: /dashboard/providers/codex
- Kiro: /dashboard/providers/kiro
- OpenRouter: /dashboard/providers/openrouter
- Ollama: /dashboard/providers/ollama
- Kimchi: /dashboard/providers/kimchi

## Scope

- Add dashboard command wiring in MainViewModel.
- Add the dashboard action to the provider card template in MainWindow.xaml.
- Introduce a shared equal-width provider action button style/layout.
- Add unit coverage for command URL construction and selected-profile validation where practical.
