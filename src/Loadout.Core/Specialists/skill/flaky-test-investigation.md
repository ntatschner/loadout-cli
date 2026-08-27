---
id: skill.flaky-test-investigation
kind: skill
title: Flaky test investigation
summary: A procedure for a test that fails sometimes.
task_phrases:
  - 'flaky'
  - 'intermittent'
  - 'fails sometimes'
  - 'fails randomly'
  - 'one run in'
  - 'passes locally'
  - 'sometimes fails'
---

## When to use

A test fails intermittently, in CI or locally.

## Procedure

1. Establish the failure rate. "Sometimes" is not a measurement; run it enough times to get a number.
2. Collect several failures and compare them. Same assertion? Same point?
3. Determine whether it fails in isolation or only in a suite. Order dependence and shared state look identical from a distance.
4. Check for time: real clocks, timeouts, sleeps, timezone.
5. Check for shared state: files, ports, databases, static fields.
6. Check for concurrency only if there is evidence of it. Flakiness is not proof of a race.
7. Check the environment: CI parallelism, machine speed, resource limits.
8. Reproduce deterministically — by seeding, by forcing order, or by constraining resources.
9. Fix the cause. Retrying or increasing a timeout hides it.
10. Re-run enough times to show the rate is now zero.

## Done when

- A measured failure rate before and after.
- The cause named, not merely suppressed.
- No retry or lengthened timeout used as the fix.
