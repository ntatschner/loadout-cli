---
id: framework.node
kind: framework
title: Node.js
summary: Event loop, streams and process lifetime.
globs:
  - '**/package.json'
  - '**/server.js'
  - '**/server.ts'
dependencies:
  - '"express"'
  - '"fastify"'
  - '"@types/node"'
task_phrases:
  - 'node.js'
  - 'nodejs'
  - 'express'
  - 'event loop'
requires:
  - 'language.typescript'
---

## Cares about

Anything that blocks the loop, and anything that keeps the process alive.

## Working rules

- Never block the event loop with synchronous I/O or heavy computation in a
  request path.
- Handle stream errors; an unhandled error event throws.
- Handle `unhandledRejection` and shut down deliberately rather than continuing
  in an unknown state.
- Prefer `AbortSignal` over ad-hoc cancellation flags.

## Pitfalls

- `JSON.parse` on an unbounded request body.
- A timer keeping the process alive after work is done.
- Backpressure ignored when piping.

## Verify

Test the failure path of every stream. Measure latency under concurrency, not
serially.
