---
name: codegraph-agent-eval
description: Benchmark CodeGraph retrieval quality on a real codebase by comparing agent behavior with vs without CodeGraph. Use when asked to test, benchmark, audit, or validate a codegraph version against a language's repo.
origin: colbymchenry/codegraph
---

# CodeGraph Quality Audit

Measures how much CodeGraph helps an agent vs plain grep/read, for a chosen version on a chosen real-world repo.

## Workflow Checklist

```
- [ ] 1. Pick version (local dev build or npm published)
- [ ] 2. Pick language (from corpus.json)
- [ ] 3. Pick repo by size
- [ ] 4. Pick harness (headless / tmux / both)
- [ ] 5. Run audit.sh in the background
- [ ] 6. Report results
```

## Harness Options

- **Headless** — `claude -p` with stream-json: exact tokens/cost, fast (2 runs)
- **Interactive (tmux)** — drives real Claude TUI in tmux: faithful behavior, metrics from session logs (slower)
- **Both** — headless + interactive (4 runs)

## Run

```bash
scripts/agent-eval/audit.sh <VERSION> <repo-name> <repo-url> "<question>" <MODE>
```

## Report

Lead with cost + tool/Read counts — they are the reliable signals. State whether codegraph reduced effort and whether both arms reached a correct answer.

Check contamination row first: `CLI calls that RETURNED output > 0` means the arm reached codegraph through Bash and numbers are void.
