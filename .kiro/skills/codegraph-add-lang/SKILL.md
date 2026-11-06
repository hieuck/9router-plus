---
name: codegraph-add-lang
description: Add tree-sitter language support to codegraph end-to-end — wire the grammar + extractor, write tests, then benchmark extraction quality. Use when adding a new language (e.g. Lua, Elixir, Zig, OCaml) to codegraph.
origin: colbymchenry/codegraph
---

# Add a Language to CodeGraph

Wire a new tree-sitter language into codegraph's extraction pipeline, prove it extracts real symbols on popular repos, and prove it beats no-codegraph for an agent.

**Never commit, push, publish, or tag** — leave all changes for the user to review.

## Workflow Checklist

```
- [ ] 1. Resolve language; bail early if already supported (just benchmark)
- [ ] 2. Find a grammar + health-check it (ABI / heap corruption)
- [ ] 3. Discover the grammar's AST node types
- [ ] 4. Wire the language (4 files; sometimes a 5th)
- [ ] 5. Build + verify-extraction loop until PASS
- [ ] 6. Add extraction tests; make them green
- [ ] 7. Auto-pick 3 popular repos by size tier; add to corpus.json
- [ ] 8. Benchmark all 3: extraction + with/without A/B
- [ ] 9. Update README + CHANGELOG
- [ ] 10. Report; do NOT commit
```

## Key Step: Grammar Health Check

Always health-check before writing an extractor:
```bash
node scripts/add-lang/check-grammar.mjs <lang> path/to/valid-sample.<ext>
```

If it FAILs (ERROR trees, ABI issues): vendor a newer build rather than using a broken grammar.
