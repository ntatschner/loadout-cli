---
id: skill.query-optimisation
kind: skill
title: Database query optimisation
summary: A procedure for making a slow query fast without changing its results.
task_phrases:
  - 'optimise query'
  - 'optimize query'
  - 'slow query'
  - 'query performance'
  - 'query is taking'
  - 'query taking'
  - 'query plan'
  - 'explain analyze'
  - 'explain analyse'
  - 'index this query'
---

## When to use

A specific query is slow and the results are correct.

## Procedure

1. Capture the query, its parameters and the observed timing.
2. Confirm the result set is correct now; you must not change it.
3. Get the actual execution plan with real row counts, not the estimate.
4. Compare estimated to actual rows. A large gap means statistics, not the query.
5. Identify the dominant cost in the plan: the scan, the sort, the nested loop.
6. Form one hypothesis: a missing index, a non-sargable predicate, a bad join order.
7. Change one thing.
8. Re-run the plan and the timing on representative data volumes.
9. Confirm the result set is byte-for-byte what it was.
10. Check what the change costs elsewhere: an index slows writes.

## Done when

- Before and after timings on the same data, quoted.
- Result set proven unchanged.
- The write cost of any new index acknowledged.
