---
name: rea-analysis-optimization
description: Optimize REA providers and shared analysis workflows for agent context, latency, CPU, and memory while preserving correctness. Use for analysis bottlenecks and cache or query design.
origin: morluto/rea
---

# REA Analysis Optimization

Find a concrete source of unnecessary work, change it at its owner, and verify equivalent completed investigations.

## Core Approach

1. **Pin the question and correctness oracle** — describe the analyst's task, choose a representative artifact, establish an independent oracle
2. **Locate the earliest unnecessary work** — trace one public CLI/MCP task through shared code to its producer, measure relevant phases
3. **Choose the smallest structural correction** based on observed cost:

| Observed cost | Candidate correction |
|---|---|
| Repeated scans of validated facts | Reuse existing order or lookup structure |
| Repeated equivalent engine work | Share a validated result or in-flight producer |
| Full reports for selected facts | Push selection to the earliest capable owner |
| Large temporary transport buffers | Decode incrementally or use bounded source reads |

4. **Compare completed tasks** — before and after with equivalent inputs, results, and Evidence semantics
5. **Limit the claim** — report task outcome, phase timings, peak memory, resource stops

## Rules

- Do not introduce an index, cache, or orchestration layer without identifying the work it removes
- Fix missing or incorrect facts before benchmarking an alternative
- Keep one interpretation and a defined reuse lifetime per cache or shared producer
