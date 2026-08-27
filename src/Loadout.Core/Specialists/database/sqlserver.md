---
id: database.sqlserver
kind: database
title: SQL Server
summary: Execution plans, indexing and locking in SQL Server.
dependencies:
  - 'System.Data.SqlClient'
  - 'Microsoft.Data.SqlClient'
  - 'sqlserver'
  - 'mssql'
task_phrases:
  - 'sql server'
  - 'sqlserver'
  - 't-sql'
  - 'tsql'
requires:
  - 'function.database'
---

## Cares about

Plan reuse, parameter sniffing and lock escalation.

## Working rules

- Read the actual execution plan, not the estimated one.
- Watch for parameter sniffing: a plan compiled for one value being reused for a
  very different one.
- Keep transactions short; lock escalation turns row locks into table locks.
- Use `SET NOCOUNT ON` in procedures called in loops.
- Prefer covering indexes over adding included columns indefinitely.

## Pitfalls

- Implicit conversion between `nvarchar` and `varchar` preventing a seek.
- `NOLOCK` used as a performance fix, silently returning dirty or duplicate rows.
- Scalar functions in a `WHERE` clause forcing row-by-row evaluation.

## Verify

Compare actual plans before and after, on representative data. Check logical
reads, not elapsed time on a warm cache.

## Defer to

`skill.query-optimisation`; `skill.safe-database-migration`.
