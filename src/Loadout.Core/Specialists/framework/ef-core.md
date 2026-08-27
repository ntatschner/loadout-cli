---
id: framework.ef-core
kind: framework
title: Entity Framework Core
summary: Query translation, tracking and migrations.
dependencies:
  - 'Microsoft.EntityFrameworkCore'
task_phrases:
  - 'ef core'
  - 'entity framework'
  - 'dbcontext'
  - 'linq query'
requires:
  - 'framework.dotnet'
---

## Cares about

What EF sends to the database, and when it decides to send it.

## Working rules

- Know what the LINQ translates to. If it cannot translate, it silently
  evaluates on the client and pulls the table.
- Use `AsNoTracking` for read-only queries.
- Load related data deliberately: `Include`, projection, or split query. Lazy
  loading in a loop is the classic N+1.
- Review generated migrations before applying them; check for accidental drops.

## Pitfalls

- `ToList()` early, turning a server-side filter into a client-side one.
- A `DbContext` shared across threads.
- A migration that renames by dropping and adding, losing the data.

## Verify

Log the generated SQL for the changed query and read it. For migrations, check
the plan against a copy, never production first.

## Defer to

The database engine specialist for plans and indexes;
`skill.safe-database-migration` for schema change.
