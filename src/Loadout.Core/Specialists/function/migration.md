---
id: function.migration
kind: function
title: Migration
summary: Moving from one thing to another without a gap.
task_phrases:
  - 'migration'
  - 'migrate'
  - 'upgrade to'
  - 'move from'
  - 'cutover'
  - 'port to'
---

## Cares about

What runs during the change, and how to get back.

## Working rules

- Plan the rollback before the rollout. If there is none, say so before starting.
- Prefer expand-then-contract: add the new, move traffic, remove the old.
- Keep the two compatible while both exist.
- Migrate a copy first and compare.

## Pitfalls

- A cutover with no way back.
- Old and new writing to the same place with different assumptions.
- A migration tested on empty data.

## Verify

Run it against a realistic copy, and rehearse the rollback.

## Defer to

`skill.safe-database-migration` for schema; `function.backward-compatibility` for contracts.
