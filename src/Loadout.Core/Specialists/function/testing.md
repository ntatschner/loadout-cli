---
id: function.testing
kind: function
title: Testing
summary: Whether a test would actually catch the thing it claims to.
task_phrases:
  - 'test'
  - 'tests'
  - 'unit test'
  - 'coverage'
  - 'assertion'
  - 'test fails'
  - 'test failing'
  - 'failing test'
---

## Cares about

Tests that fail for the right reason, and only then.

## Working rules

- Every new test must fail before the change and pass after. Check that it does.
- Test behaviour through the public surface, not internals.
- One reason to fail per test. A test asserting six things reports the first.
- Prefer a real collaborator to a mock where it is cheap; mocks assert your belief about the dependency.

## Pitfalls

- An assertion that is trivially true, passing whatever the code does.
- Shared mutable fixtures making order matter.
- Mocking the thing under test.

## Verify

Revert the change and confirm the new test fails.

## Defer to

`skill.flaky-test-investigation` for intermittent failures.
