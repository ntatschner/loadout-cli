---
id: skill.secure-code-review
kind: skill
title: Secure code review
summary: A procedure for reviewing a change for security consequences.
task_phrases:
  - 'security review'
  - 'review authentication'
  - 'review the auth'
  - 'security audit'
  - 'is this secure'
  - 'review permissions'
  - 'authentication'
  - 'authorisation'
  - 'authorization'
  - 'auth changes'
modes:
  - 'review'
  - 'investigate'
---

## When to use

A change touches authentication, authorisation, input handling or secrets.

## Procedure

1. Identify the trust boundaries the change crosses.
2. List every input that reaches the changed code, and where it comes from.
3. For each, check validation, and check it happens server-side.
4. Check output encoding at each sink, for that sink's context.
5. Check authorisation is applied per request against the acting identity.
6. Check secrets: none logged, none in errors, none committed, none in the response.
7. Check the failure path. Errors often leak more than successes.
8. Look for the same pattern elsewhere; a fixed path with an unfixed sibling is not fixed.
9. Report each finding with the concrete attack: input, path, effect.

## Done when

- Every input traced to a validation point.
- Findings expressed as an attack, not as a worry.
- Sibling code paths checked for the same defect.
