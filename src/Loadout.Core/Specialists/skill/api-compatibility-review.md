---
id: skill.api-compatibility-review
kind: skill
title: API compatibility review
summary: A procedure for deciding whether a change breaks callers.
task_phrases:
  - 'breaking change'
  - 'api compatibility'
  - 'will this break'
  - 'compatibility review'
modes:
  - 'review'
  - 'investigate'
  - 'advise'
---

## When to use

A published interface, format or contract is changing.

## Procedure

1. Identify the contract surface: signatures, wire formats, stored formats, defaults, errors.
2. Classify each change: addition, removal, rename, widening, narrowing.
3. Additions are usually safe. Everything else needs a caller check.
4. Find the callers you know about and check each.
5. Consider callers you do not control, and stored data written by the old version.
6. For anything breaking, decide: version it, deprecate it, or accept and document it.
7. Check the error contract as carefully as the success one.
8. Write down the semantics that changed, in the release notes.

## Done when

- Every change classified.
- Breaking changes either versioned, deprecated or explicitly accepted.
- Stored data written by the previous version still readable.
