---
id: skill.dependency-upgrade
kind: skill
title: Safe dependency upgrade
summary: A procedure for updating dependencies without a surprise.
task_phrases:
  - 'upgrade dependency'
  - 'update packages'
  - 'bump dependencies'
  - 'dependabot'
  - 'npm update'
---

## When to use

One or more dependencies need updating.

## Procedure

1. List what is being updated and from which version to which.
2. Read the changelog between the two, especially for breaking changes.
3. Update one significant dependency at a time.
4. Run the full test suite after each.
5. Check transitive changes in the lock file, not just the direct one.
6. Check for licence changes.
7. Exercise the paths that actually use the dependency, not only the unit tests.
8. Keep the lock file change in its own commit.

## Done when

- Each significant dependency updated and tested separately.
- Changelogs read, not assumed.
- Transitive changes reviewed.
