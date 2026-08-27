---
id: database.sqlite
kind: database
title: SQLite
summary: Concurrency limits, typing and file-level behaviour.
globs:
  - '**/*.db'
  - '**/*.sqlite'
  - '**/*.sqlite3'
dependencies:
  - 'sqlite'
  - 'Microsoft.Data.Sqlite'
  - 'better-sqlite3'
task_phrases:
  - 'sqlite'
requires:
  - 'function.database'
---

## Cares about

That it is a file, and that writers do not share well.

## Working rules

- One writer at a time. Enable WAL where concurrent reads matter.
- Set a busy timeout; the default gives up immediately.
- Remember dynamic typing: a column type is a hint, not a constraint.
- Foreign keys are off unless switched on per connection.

## Pitfalls

- Treating it as a client-server database under concurrent load.
- A transaction held open across user interaction, blocking every writer.
- Assuming `INTEGER PRIMARY KEY` behaves like a normal column.

## Verify

Test with the concurrency the application really has, not serially.

## Defer to

`function.database` for schema design.
