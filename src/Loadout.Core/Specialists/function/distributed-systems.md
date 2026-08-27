---
id: function.distributed-systems
kind: function
title: Distributed systems
summary: Partial failure, retries and ordering across a network.
task_phrases:
  - 'distributed'
  - 'microservice'
  - 'retry'
  - 'idempotent'
  - 'message queue'
  - 'eventual consistency'
---

## Cares about

What happens when one part is slow, gone, or answering twice.

## Working rules

- Every remote call can fail, hang or succeed after you gave up. Design for all three.
- Retries need idempotency, a bound, and backoff with jitter.
- Timeouts must be shorter going in than coming out, or they stack.
- Do not assume ordering across independent paths.

## Pitfalls

- A retry storm turning a slow dependency into an outage.
- At-least-once delivery treated as exactly-once.
- A distributed transaction assumed where there is none.

## Verify

Test with the dependency slow and with it absent, not only healthy.

## Defer to

`function.reliability` for operational posture.
