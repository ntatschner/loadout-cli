---
id: function.release
kind: function
title: Release and packaging
summary: What ships, and whether it can be installed and undone.
task_phrases:
  - 'release'
  - 'version bump'
  - 'changelog'
  - 'package'
  - 'installer'
  - 'ship'
---

## Cares about

Reproducibility, versioning and the upgrade path.

## Working rules

- Version deliberately: what changed decides the number, not how the work felt.
- Write the notes from the actual commit range.
- Test the upgrade path from the previous version, not just a clean install.
- Ship the same artefact you tested.

## Pitfalls

- Release notes claiming work that shipped earlier.
- An installer that fails over a running application.
- A version bump without the artefact rebuilt.

## Verify

Install over the previous version on a real machine, and check the version reported.

## Defer to

`skill.release-validation` for the checklist.
