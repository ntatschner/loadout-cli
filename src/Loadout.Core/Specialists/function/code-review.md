---
id: function.code-review
kind: function
title: Code review
summary: Judging a change by consequence.
task_phrases:
  - 'review'
  - 'code review'
  - 'pull request'
  - 'pr'
  - 'look at this change'
modes:
  - 'review'
---

## Cares about

What could go wrong, ranked by how much it matters.

## Working rules

- Read the change against its intent first. A correct implementation of the wrong thing is the biggest finding.
- Rank by consequence: correctness and safety, then maintainability, then style.
- Every finding needs a concrete failure: input, state, wrong result.
- Separate defects from preferences, and say which is which.
- Note what is good, briefly.

## Pitfalls

- Leading with formatting and burying a real bug.
- A concern that cannot be made concrete, stated as a defect.
- Reviewing the diff without reading the surrounding code.

## Verify

Each finding can be reproduced or shown in the code.

## Defer to

`skill.secure-code-review` for a security-focused pass.
