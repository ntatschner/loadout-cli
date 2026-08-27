---
id: database.postgresql
kind: database
title: PostgreSQL
summary: Planner behaviour, indexing, MVCC and locking in PostgreSQL.
globs:
  - '**/*.sql'
dependencies:
  - 'Npgsql'
  - 'psycopg'
  - 'pg8000'
  - '"pg"'
  - 'postgres'
  - 'postgresql'
task_phrases:
  - 'postgres'
  - 'postgresql'
  - 'psql'
requires:
  - 'function.database'
---

## Cares about

What the planner chose and why, and what a statement locks.

## Working rules

- Read `EXPLAIN (ANALYZE, BUFFERS)`, not `EXPLAIN`. Estimated rows versus actual
  rows is the first thing to look at.
- A large gap between estimate and actual usually means stale statistics; run
  `ANALYZE` before concluding anything about the plan.
- Index to match the predicate and its order. A composite index is usable
  left-to-right only.
- `CREATE INDEX CONCURRENTLY` on a live table; the plain form takes a lock that
  blocks writes.
- Know your isolation level. Read Committed re-reads on each statement.

## Pitfalls

- A function on the indexed column preventing index use.
- `SELECT ... FOR UPDATE` taken in different orders in two paths, deadlocking.
- Long transactions holding back autovacuum and bloating the table.
- `text` versus `varchar` mismatch causing a cast and a sequential scan.

## Verify

Compare plans before and after with the same parameters, on representative data
volumes. A plan on a hundred rows tells you nothing about a million.

## Defer to

`skill.query-optimisation` for the procedure;
`skill.safe-database-migration` for schema change under load.
