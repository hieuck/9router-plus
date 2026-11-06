---
name: diagnosing-superpowers
description: Use when a session went wrong and your human partner wants to know why — repeated work, ignored plans, stumbles, poor results, a skill that didn't fire, "it took too long", "why is it so expensive"
origin: obra/superpowers
---

# Diagnosing Superpowers

## Overview

Pin down what went wrong in a session, read the transcripts, and report with evidence.

**Core principle:** Every finding cites `path:line`. No citation, no finding. Every number comes from the transcript or a command you ran — never from memory.

## Workflow

1. **Problem intake** — get a specific problem statement (session, turn range, what expected vs. what happened)
2. **Locate** — find the session transcript(s) on disk
3. **Triage** — read the region around the reported problem
4. **Report** — findings with file and line references

## Hard Rules

- **Read-only** — never modify, move, or delete a session file
- **Human prompts only** — hook output and tool results are not your partner's words
- **No diagnosis** — report what happened with evidence; don't name defects or propose changes to skills
- **Intake before analysis** — nothing starts until the problem statement is scoped

## Red Flags

| Thought | Reality |
|---------|---------|
| "The problem is obvious, skip intake" | The statement scopes everything. Ask. |
| "Small, targeted edit, no restructuring needed" | Not your call. Report the evidence. |
| "The price per token is well known" | Numbers not computed from transcript are invented. |
