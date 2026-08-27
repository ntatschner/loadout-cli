---
id: function.api
kind: function
title: API design
summary: Contracts, versioning and what a caller can rely on.
task_phrases:
  - 'api'
  - 'endpoint'
  - 'contract'
  - 'rest'
  - 'graphql'
  - 'openapi'
  - 'breaking change'
---

## Cares about

What a caller may depend on, and what breaks them.

## Working rules

- Decide what is part of the contract and what is incidental. Document the first.
- Adding is usually safe. Removing, renaming and narrowing are not.
- Errors are part of the contract: status codes, shapes and meanings.
- Be conservative in what you send; a response nobody asked for still becomes relied upon.

## Pitfalls

- A field removed because nothing internal used it.
- An error changing from 404 to 200 with an error body.
- Optional made required, or a default changed.

## Verify

Check a real caller still works against the change, not just the tests.

## Defer to

`skill.api-compatibility-review`; `function.backward-compatibility`.
