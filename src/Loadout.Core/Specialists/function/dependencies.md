---
id: function.dependencies
kind: function
title: Dependencies
summary: What a new or updated package brings with it.
globs:
  - '**/package-lock.json'
  - '**/yarn.lock'
  - '**/Cargo.lock'
  - '**/poetry.lock'
task_phrases:
  - 'dependency'
  - 'dependencies'
  - 'upgrade package'
  - 'npm audit'
  - 'vulnerable package'
  - 'bump version'
---

## Cares about

Transitive weight, licence and maintenance risk.

## Working rules

- Read what a dependency pulls in, not just what it does.
- Prefer the standard library or a dozen lines to a package for something small.
- Check licence compatibility before adopting.
- Update deliberately, one significant dependency at a time, with tests between.

## Pitfalls

- A minor version bump carrying a breaking change.
- A lock file updated wholesale in a change about something else.
- An unmaintained package adopted for one convenience function.

## Verify

Full test suite after each significant update, not after all of them.

## Defer to

`skill.dependency-upgrade` for the procedure.
