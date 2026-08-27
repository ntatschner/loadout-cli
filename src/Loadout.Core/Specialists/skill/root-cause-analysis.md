---
id: skill.root-cause-analysis
kind: skill
title: Root cause analysis
summary: A procedure for getting from a symptom to the actual cause.
task_phrases:
  - 'root cause'
  - 'why did'
  - 'what caused'
  - 'keeps happening'
---

## When to use

Something is failing and the cause is not obvious.

## Procedure

1. State observed versus expected, precisely and in one sentence each.
2. Reproduce it. If you cannot, say so and work out what would make it reproducible.
3. Capture the exact failure: message, exit code, stack, timestamps.
4. Find the first point where actual diverges from expected. Bisect rather than read.
5. Generate at least two hypotheses; a single hypothesis is a conclusion in disguise.
6. Test each by prediction: what would be true if this were the cause?
7. Establish the cause by making the failure appear and disappear on demand.
8. Write a regression test that fails now.
9. Apply the smallest repair that addresses the cause, not the symptom.
10. Verify: the regression test passes, the covering tests pass, and the original reproduction is gone.

## Done when

- The failure can be produced and removed on demand.
- A test exists that would have caught it.
- The cause is stated, with the evidence for it.
