---
id: skill.safe-database-migration
kind: skill
title: Safe database migration
summary: A procedure for changing a schema without losing data or availability.
task_phrases:
  - 'schema migration'
  - 'alter table'
  - 'add column'
  - 'database migration'
  - 'change the schema'
---

## When to use

A schema change is needed on a database that holds real data.

## Procedure

1. State what the change is and what happens to existing rows.
2. Establish whether the change is reversible. If not, say so before proceeding.
3. Confirm a backup exists and is recent.
4. Prefer expand-then-contract: add the new shape, backfill, switch reads, then remove the old.
5. Check what the statement locks and for how long, at production data volume.
6. Write the rollback, and rehearse it.
7. Run the migration against a restored copy of production.
8. Compare row counts and spot-check data after the copy run.
9. Apply, watching for lock waits and errors.
10. Verify the application against the new schema before removing anything.

## Done when

- Rollback written and rehearsed.
- Run against a production-sized copy first.
- Nothing dropped in the same step that added its replacement.
