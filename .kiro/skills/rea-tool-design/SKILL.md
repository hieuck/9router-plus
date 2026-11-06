---
name: rea-tool-design
description: Audit, investigate, design, or change REA investigation tools, CLI/MCP contracts, provider capabilities, and Evidence semantics.
origin: morluto/rea
---

# REA Tool Design

Inspect the current contract and nearest existing tool, then design or audit.

## For an Audit

Scope representative tasks, exercise the live workflow, and distinguish:
- Reproduced bugs
- Supported limitations
- Design opportunities
- Unverified claims

Check rendered MCP prompts and guided workflows against live contracts.

## For a Design Request

Return:
- The proposed boundary
- Input/result shape
- Evidence semantics
- Nearest alternative
- Verification approach

Implement when requested, preserving the user's scope and existing authorization.

## Rules

- Inspect the current contract before proposing changes
- Source, documentation, and live catalogs may describe different versions
- Exercise the analyst's task through REA and compare with native provider observations where available
- Report working alternatives and evidence limits as well as findings
