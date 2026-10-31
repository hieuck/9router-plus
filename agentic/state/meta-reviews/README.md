# Meta-reviews

This directory stores evidence for material changes to `AGENTS.md` or `agentic/*.md`.

## Required evidence for material changes
- `meta-review-YYYY-MM-DD-HHMMSS.md` (or similar) containing:
  - Diff summary
  - Tier: high-risk
  - Reviewers: model + sessionID for each (>=3, independent)
  - Verdicts (PASS/FAIL) + findings
  - Substitutions (if any) + justification
  - Resolution notes
  - Conclusion (APPROVE/NEEDS_FIXES/REJECT)

## Trivial exemption log
If claiming trivial-only exemption, create `trivial-exemption-YYYY-MM-DD-HHMMSS.md` with:
- Files changed
- Rationale (why pure formatting/typo/link)
- Diff summary
- Logged by (coordinator/subagent)
- Reviewable for abuse
