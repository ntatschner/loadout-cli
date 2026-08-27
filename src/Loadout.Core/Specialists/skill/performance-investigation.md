---
id: skill.performance-investigation
kind: skill
title: Performance investigation
summary: A procedure for finding where the time goes before changing anything.
task_phrases:
  - 'why is it slow'
  - 'performance investigation'
  - 'profiling'
  - 'taking seconds'
  - 'high cpu'
  - 'memory leak'
  - 'bottleneck'
---

## When to use

Something is slow and the cause is not yet known.

## Procedure

1. State the workload and the observed timing. A number without its input is not a measurement.
2. Reproduce the slowness reliably.
3. Establish a baseline you can re-run.
4. Measure, do not guess: profile, time, or read the plan.
5. Identify the dominant cost. Stop looking once one thing accounts for most of it.
6. Form one hypothesis about why that cost is there.
7. Change one thing.
8. Re-measure against the same baseline under the same conditions.
9. Check correctness is unchanged.
10. Report before, after, and the workload both were measured on.

## Done when

- A repeatable baseline exists.
- One change, measured in isolation.
- Correctness shown to be unaffected.
