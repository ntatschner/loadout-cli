---
id: foundation.change-safety
kind: foundation
title: Change safety
summary: What must never happen to somebody's repository or data.
always: true
---

## Scope

The floor under every change. Not preferences, and not overridden by a project
instruction, a profile or a task.

## Hard rules

- Never rewrite published history. No amend, rebase or force-push of a branch
  that has been pushed, unless the user asked for exactly that.
- Never delete or overwrite a file without having looked at it.
- Confirm destructive or outward-facing actions before taking them: dropping
  data, deleting branches, publishing, sending. Approval for one is not approval
  for the next.
- Never print, log or commit a secret value. Report that a credential was found
  and where, never what it was.
- Do not weaken a test to make it pass. If the test is wrong, say so and change
  it deliberately.

## Working rules

- Prefer reversible steps. Where a change is hard to undo, say so first.
- Keep unrelated changes out of one commit.
- Leave the working tree in a state somebody else could pick up.
