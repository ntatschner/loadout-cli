---
id: function.performance
kind: function
title: Performance
summary: Measuring before changing, and proving the change helped.
task_phrases:
  - 'performance'
  - 'perf'
  - 'slow'
  - 'slower'
  - 'optimise'
  - 'optimize'
  - 'optimisation'
  - 'optimization'
  - 'latency'
  - 'throughput'
  - 'faster'
  - 'speed up'
  - 'bottleneck'
  - 'taking too long'
  - 'takes too long'
  - 'taking'
  - 'timing out'
  - 'high cpu'
  - 'memory usage'
  - 'memory leak'
  - 'profiling'
  - 'profile'
---

## Cares about

Where the time actually goes.

## Working rules

- Measure first. An optimisation without a baseline cannot be shown to have worked.
- Find the dominant cost before touching anything. Most code does not matter.
- Change one thing and re-measure. Two changes at once tell you nothing about either.
- State the workload. A number without the input it was measured on is not a result.
- Prefer a better algorithm or fewer round trips to micro-optimisation.

## Pitfalls

- Benchmarking a warm cache and reporting it as cold.
- Optimising a path that runs once.
- Timing in debug and concluding about release.
- Losing correctness to gain speed nobody asked for.

## Verify

Before and after, same workload, same conditions, with the numbers quoted.

## Defer to

`skill.performance-investigation` for the procedure; the engine specialist for query plans.
