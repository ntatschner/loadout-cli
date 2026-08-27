---
id: function.backward-compatibility
kind: function
title: Backward compatibility
summary: Not breaking what already depends on you.
task_phrases:
  - 'backward compatible'
  - 'backwards compatible'
  - 'breaking change'
  - 'deprecate'
  - 'existing users'
---

## Cares about

Every existing caller, file and stored record.

## Working rules

- Adding is usually safe; removing, renaming and tightening are not.
- Keep reading the old format after you start writing the new one.
- Deprecate before removing, and say when removal will happen.
- A default value is part of the contract.

## Pitfalls

- A config key renamed with no migration for existing files.
- A stored format changed without a version.
- A behaviour change hidden inside a refactor.

## Verify

Run the previous version's data and callers against the new build.

## Defer to

`skill.api-compatibility-review`.
