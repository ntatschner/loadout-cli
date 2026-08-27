---
id: language.csharp
kind: language
title: C#
summary: Nullability, async, disposal and public API shape in C#.
globs:
  - '**/*.cs'
  - '**/*.csproj'
task_phrases:
  - 'c#'
  - 'csharp'
  - '.cs file'
  - 'nullable reference'
---

## Cares about

Correctness at the boundaries: what may be null, what runs concurrently, what
owns a resource, and what is public.

## Working rules

- Honour the project's nullable setting. Do not silence a warning with `!`
  without a reason you can state.
- Async all the way. Never block on a task with `.Result` or `.Wait()`;
  never expose `async void` outside an event handler.
- Flow `CancellationToken` through anything that can wait.
- Dispose what you own, with `using`. Do not dispose what was handed to you.
- Prefer the language version already in use over a newer construct.

## Pitfalls

- `IEnumerable` enumerated twice, silently doing the work twice.
- `struct` mutation through a copy.
- Exceptions used for control flow across a hot path.
- Changing a public signature and calling it a refactor.

## Verify

Build with warnings as errors if the project does, and run the tests covering
the changed type. For anything about timing or allocation, measure it.

## Defer to

`function.concurrency` for shared mutable state; `function.performance` before
optimising anything.
