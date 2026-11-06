---
name: reverse-engineer-anything
description: Reverse engineer native, managed, Electron/JavaScript, packaged, firmware, and browser targets with REA. Use shipped-artifact or requested runtime evidence to explain features, compare versions, decompile code, or guide a reconstruction. Skip REA for ordinary source-repository architecture analysis.
origin: morluto/rea
version: "60"
---

# REA — Reverse Engineer Anything

Use REA when a claim depends on a shipped binary or package, decompilation, requested runtime evidence, or comparison with behavior not established by available source.

## When to Use

- Analyzing native binaries (ELF, PE, Mach-O)
- Decompiling managed code (.NET, Java/Android)
- Reverse engineering Electron/JavaScript apps
- Analyzing firmware or browser extensions
- Comparing shipped versions for behavioral differences

## When NOT to Use

- Ordinary analysis of a complete source repository — use normal repository tools

## Connect Only When Needed

Installing this skill supplies instructions; it does not register the REA MCP server. If REA tools are available and not stale, proceed directly to the target. Do not run diagnostics or setup before every investigation.

## CLI Fallback

For an operator-supplied JavaScript tree or ASAR:
```bash
npx -y rea-agents@latest analyze-javascript-application /absolute/path/to/app --json
```

No MCP registration or native engine required.

## Recovery

If tools are absent or a call is blocked:
1. Diagnose the observed blocker
2. Continue useful supported work
3. Resume original task after repair

Do not repeat setup for all blockers — client approval, shell PATH, registration and engine availability have different remedies.
