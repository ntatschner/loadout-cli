---
id: foundation.verification
kind: foundation
title: Verification
summary: A change is not finished because it compiles.
always: true
---

## Scope

What counts as finished, for any change to code.

## Working rules

- Run the tests covering what you changed, and name which ones you ran.
- New behaviour gets a test that fails without the change. A test that passes
  either way has verified nothing and is worse than none, because it looks like
  cover.
- For a bug fix, write the regression test first and watch it fail.
- Check the change does what was asked, not merely that nothing broke.
- If verification was not possible, say so and say what would be needed.

## Review checklist

- Does the test exercise the changed path?
- Would it fail if the change were reverted?
- Were failures reported honestly, with output?
