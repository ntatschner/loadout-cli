---
id: function.legacy-code
kind: function
title: Legacy code
summary: Working safely in code with no tests and no author.
task_phrases:
  - 'legacy'
  - 'old code'
  - 'no tests'
  - 'inherited'
  - 'nobody knows'
---

## Cares about

Getting a foothold before changing anything.

## Working rules

- Characterise first: write a test that records what it currently does, right or wrong.
- Find a seam where behaviour can be observed or substituted without a large change.
- Change in small, reversible steps.
- Do not tidy on the way past. Separate the fix from the cleanup.

## Pitfalls

- Assuming existing behaviour is a bug when something depends on it.
- A rewrite begun because the code is unpleasant rather than because it is wrong.
- Removing a workaround whose reason is not recorded.

## Verify

The characterisation test still passes, or you can say exactly what changed and why.
