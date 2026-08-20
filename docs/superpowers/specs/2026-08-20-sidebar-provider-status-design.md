# Sidebar and Provider Status Design
**Date:** 2026-08-20
## Goal
Improve the collapsed profile sidebar alignment and make the selected profile's provider health visible directly on every provider card.
## Decisions
- In collapsed mode, the selected/hover border follows the same centered item bounds as the avatar instead of stretching into the asymmetric content area left by the vertical scrollbar.
- The provider card header keeps the existing workflow badge and adds a compact status badge beside it.
- Status mapping follows the existing health resolver:
  - Online: healthy connection, green.
  - Disable: matching connection exists but is inactive, yellow.
  - Error: a matching connection reports an error, red.
  - Not added: no matching connection, gray.
  - Checking: status has not been synchronized yet, muted gray.
- The card status is derived from the currently selected Chrome profile; changing profile updates all cards.
- The status badge exposes the existing detailed health tooltip so error details remain available without making cards taller.
## Scope
- Adjust ProfileListItemStyle and collapsed profile item layout in MainWindow.xaml.
- Add a small display-state model for provider cards and synchronize it from ProfileRowViewModel.
- Add unit coverage for the display-state mapping.
- Keep existing connection matching and health resolution unchanged.
