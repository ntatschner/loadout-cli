---
id: function.reliability
kind: function
title: Reliability
summary: Failure modes, degradation and recovery.
task_phrases:
  - 'reliability'
  - 'availability'
  - 'outage'
  - 'failover'
  - 'resilience'
  - 'circuit breaker'
---

## Cares about

What the system does when a dependency is unavailable.

## Working rules

- Decide what degraded looks like, rather than letting it be undefined.
- Fail fast where a slow failure is worse than a quick one.
- Make recovery automatic where it is safe, and obvious where it is not.
- A backup nobody has restored from is not a backup.

## Pitfalls

- A health check that only checks the process is running.
- Cascading failure from a shared thread pool.
- Retry logic with no circuit breaker.

## Verify

Turn the dependency off and watch what happens.
