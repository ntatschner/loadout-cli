---
id: skill.bug-investigation
kind: skill
title: Bug investigation
summary: A procedure for triaging and understanding a reported defect.
task_phrases:
  - 'bug report'
  - 'investigate'
  - 'reported issue'
  - 'defect'
---

## When to use

A defect has been reported and needs understanding before it is fixed.

## Procedure

1. Establish what was expected and what happened, from the report.
2. Establish the environment: version, platform, configuration.
3. Reproduce on the reported version before doing anything else.
4. Narrow to the smallest reproduction.
5. Determine whether it is a regression, and if so bisect to the change.
6. Identify the faulty code path and read the surrounding code.
7. Decide severity and blast radius: who else is affected, and is data at risk?
8. Report the finding before fixing, unless the fix is obvious and small.

## Done when

- The defect reproduces on demand.
- The offending change or code path is named.
- Severity and affected versions are stated.
