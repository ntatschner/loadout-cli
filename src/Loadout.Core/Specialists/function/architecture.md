---
id: function.architecture
kind: function
title: Architecture
summary: Boundaries, coupling and the cost of a decision that is hard to undo.
task_phrases:
  - 'architecture'
  - 'design decision'
  - 'should we split'
  - 'coupling'
  - 'boundaries'
---

## Cares about

Where the seams are, and which decisions are expensive to reverse.

## Working rules

- Identify which decisions are one-way and which are cheap to change. Spend the care on the first kind.
- Prefer the design that keeps the expensive decision deferred.
- A boundary earns its cost by isolating change. A boundary that never changes independently is overhead.
- Say what the design gives up. A design with no trade-off has not been understood.

## Pitfalls

- Abstraction added for a second implementation that never arrives.
- Layers that all change together for every feature.
- A shared library coupling services that should have been able to diverge.

## Verify

Describe a concrete change the design is meant to make cheap, and trace it.

## Defer to

`function.distributed-systems` where the boundary is a network.
