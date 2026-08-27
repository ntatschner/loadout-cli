---
id: framework.fastapi
kind: framework
title: FastAPI
summary: Dependency injection, validation and async correctness.
dependencies:
  - 'fastapi'
task_phrases:
  - 'fastapi'
requires:
  - 'language.python'
---

## Cares about

What the schema promises and what the handler actually does.

## Working rules

- Let Pydantic validate at the boundary; do not re-check by hand inside handlers.
- A `def` handler runs in a threadpool and an `async def` one does not. Do not
  call blocking I/O from an async handler.
- Use dependencies for shared setup so it can be overridden in tests.
- Declare response models so the schema and the response cannot drift.

## Pitfalls

- A blocking database driver inside an async endpoint, serialising the app.
- Exceptions turned into 500s where a 4xx was meant.
- Mutable default dependency state shared across requests.

## Verify

Exercise the endpoint through the test client, including validation failures.

## Defer to

`function.api` for contract design; `function.security` for auth.
