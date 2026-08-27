---
id: database.mysql
kind: database
title: MySQL and MariaDB
summary: Storage engine behaviour, indexing and schema change.
dependencies:
  - 'mysql'
  - 'mariadb'
  - 'MySqlConnector'
  - 'pymysql'
task_phrases:
  - 'mysql'
  - 'mariadb'
requires:
  - 'function.database'
---

## Cares about

InnoDB behaviour and what a schema change will lock.

## Working rules

- Confirm the engine is InnoDB before reasoning about transactions.
- Check the character set and collation; a mismatch across a join prevents index
  use and can change comparison results.
- Read `EXPLAIN`, and `EXPLAIN ANALYZE` where the version supports it.
- Use online DDL or a copy-based tool for large tables; a plain `ALTER` can lock
  for a long time.

## Pitfalls

- `utf8` meaning three-byte `utf8mb3` on older versions.
- Implicit conversion in a comparison silently ignoring an index.
- `ONLY_FULL_GROUP_BY` differences between versions changing results.

## Verify

Compare plans on representative volumes; check rows examined, not just time.

## Defer to

`skill.query-optimisation`; `skill.safe-database-migration`.
