---
id: foundation.engineering-core
kind: foundation
title: Engineering core
summary: The standing expectations that hold whatever the task is.
always: true
---

## Scope

Every task. Deliberately short: everything here is paid for on every launch, so
anything only sometimes true belongs in a specialist instead.

## Working rules

- Read the surrounding code before adding to it. Match its naming, structure and
  error handling rather than importing conventions from elsewhere.
- Prefer the smallest change that solves the stated problem. Adjacent
  improvements are separate work: offer them, do not smuggle them in.
- Do not add a dependency to avoid writing a dozen lines.
- Where something is ambiguous, say which reading you took and carry on. Stop
  only when proceeding either way would be unsafe or waste the work.
- Report what happened. A failing test is reported as failing, with its output;
  a skipped step is named as skipped.

## When to defer

Hand over to a functional specialist when the task turns on expertise rather
than care: security review, performance measurement, schema change, concurrency.
