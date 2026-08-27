---
id: skill.release-validation
kind: skill
title: Release validation
summary: A checklist for confirming a release is fit to ship.
task_phrases:
  - 'cut a release'
  - 'release checklist'
  - 'ready to ship'
  - 'validate the release'
---

## When to use

A release is about to be cut or has been built.

## Procedure

1. Confirm the version number matches what actually changed in the commit range.
2. Write the notes from that range, not from memory.
3. Confirm the artefact was built from the tagged commit.
4. Run the full test suite on the release configuration.
5. Install on a clean machine.
6. Install over the previous version, and check the upgrade path.
7. Verify the version the installed build reports.
8. Check the artefact is signed where signing applies.
9. Confirm the rollback: can the previous version be reinstalled?

## Done when

- Upgrade path tested, not only clean install.
- Notes derived from the commit range.
- Reported version matches the tag.
