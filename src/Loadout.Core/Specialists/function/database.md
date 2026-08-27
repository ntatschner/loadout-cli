---
id: function.database
kind: function
title: Database
summary: Schema design, transactions and data safety, independent of engine.
task_phrases:
  - 'database'
  - 'schema'
  - 'migration'
  - 'transaction'
  - 'query'
  - 'sql'
  - 'index'
---

## Cares about

Correctness of the data, and what a change does to what is already there.

## Working rules

- Decide the transaction boundary deliberately, and keep it as short as the invariant allows.
- Know the isolation level you are relying on, and say so.
- A schema change is a data change. Ask what happens to existing rows.
- Constrain in the database as well as the application. The application is not the only writer.
- Index for the predicate, not for the column that looked important.

## Pitfalls

- A read-modify-write with no locking, lost under concurrency.
- A migration that is not reversible and was not backed up.
- Nullable columns added without deciding what null means.

## Verify

Test against representative data volumes and with more than one connection.

## Defer to

The engine specialist for plans and locking; `skill.safe-database-migration` for schema change.
