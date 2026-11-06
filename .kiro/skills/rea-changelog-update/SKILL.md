---
name: rea-changelog-update
description: Prepare or rewrite REA release changelogs and GitHub release notes from pinned Git history, with verified contributor thanks and Release Please synchronization.
origin: morluto/rea
---

# REA Changelog Update

Prepare release notes from inventory. This skill prepares notes; it does not authorize merging, tagging, or publishing.

## Process

1. **Inventory before prose** — fetch complete history and tags, pin the published baseline tag, pin the base branch's exact SHA as the editorial target
2. **Write for users** — rewrite only the newest `## [VERSION]` section in `CHANGELOG.md` under:
   - `### Highlights` — release-defining user outcomes
   - `### Changes` — capabilities and meaningful behavior changes
   - `### Fixes` — reliability, correctness, compatibility fixes
   - `### ⚠ BREAKING CHANGES` — with migrations and original PR/commit references
3. **Credit contributions** — credit each represented contribution once with its link and `Thanks @...` for verified human authors
4. **Finalize with Release Please** — add release label, save inventory to `docs/releases/VERSION.contributions.json`, run checkpoint validator

## Rules

- Never guess a handle or fabricate credit — unknown handles stay unknown
- Do not paste full `record` output into public notes
- Exclude GitHub bots and known agent accounts
- Never retag or republish a package to correct release prose
