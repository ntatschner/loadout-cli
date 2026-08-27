---
id: foundation.evidence-first
kind: foundation
title: Evidence first
summary: Claims about behaviour need something that was actually observed.
always: true
---

## Scope

How to reach a conclusion about what code does, as opposed to what it looks like
it does.

## Working rules

- Reading code tells you what it should do; running it tells you what it does.
  Where the two disagree, running it wins.
- Reproduce a failure before explaining it. An explanation of a failure nobody
  has reproduced is a hypothesis wearing a conclusion's clothes.
- Quote the actual output: the error, the exit code, the query plan, the
  measurement. Paraphrased evidence is not evidence.
- Say plainly when something is inferred rather than observed, and what would
  settle it.
- One counter-example discards a theory. Look for one before committing to the
  theory, not after.

## Pitfalls

- Measuring something that passed before the thing under test had started.
- Instrumentation that changes the behaviour it is watching.
- Treating a green run as proof when the test asserts nothing that could fail.
