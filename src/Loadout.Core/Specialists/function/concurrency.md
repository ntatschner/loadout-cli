---
id: function.concurrency
kind: function
title: Concurrency
summary: Shared mutable state, ordering and deadlock.
task_phrases:
  - 'concurrency'
  - 'race condition'
  - 'deadlock'
  - 'thread'
  - 'lock'
  - 'async'
  - 'parallel'
  - 'mutex'
---

## Cares about

What two threads can do at the same moment.

## Working rules

- Identify the shared mutable state. If there is none, there is no race.
- Take locks in one order everywhere. Two orders is a deadlock waiting for load.
- Do not hold a lock across an await or a remote call.
- Prefer immutability or message passing to locking.

## Pitfalls

- A check followed by an act, with a gap between them.
- A collection read while another thread writes it.
- `async void` losing an exception.
- A test that passes because it is fast enough today.

## Verify

Run under a race detector where the platform has one, and under real concurrency.
