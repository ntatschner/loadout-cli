---
id: language.sql
kind: language
title: SQL
summary: Set-based thinking, null semantics and query correctness.
globs:
  - '**/*.sql'
task_phrases:
  - 'sql'
  - 'sql query'
  - 'select statement'
  - 'stored procedure'
---

## Cares about

Whether the query returns the right rows, before whether it returns them
quickly.

## Working rules

- Think in sets, not loops. A correlated subquery per row is usually a join.
- `NULL` is not a value and does not equal itself. Use `IS NULL`, and know what
  it does to `NOT IN`.
- Be explicit about join type. An accidental cross join is a correctness bug.
- Name columns in `INSERT` and never rely on `SELECT *` in application code.
- Parameterise. String-concatenated SQL is an injection, not a style choice.

## Pitfalls

- `NOT IN` with a nullable subquery returning nothing.
- Aggregates over an outer join counting the null row.
- Implicit type conversion silently preventing index use.

## Verify

Run against representative data and check the row count, not just that it ran.

## Defer to

The engine specialist for anything about plans or locking;
`function.database` for schema and transaction design.
