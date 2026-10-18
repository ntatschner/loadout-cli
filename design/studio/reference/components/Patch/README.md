# Patch

Patch shows what one node changed: its branch, the commit it was measured from, a file summary first, the diff, and **Open a pull request**, the thing anybody does next after reading one.

## Open a pull request

- `$ loadout team pr <run> --node implementer/1 --draft`. It pushes the node's branch and opens the pull request with `gh`, using the GitHub login already there. The body carries the run's goal, which node did it, what the checker made of its report, what it cost, and the node's own words.
- It is the one button on the page that reaches a remote, so it opens a Dialog first that names the branch, with "Open it as a draft" (on by default) and a title (the run's goal unless given). "Say what it would do" runs the same with `--dry-run`.
- `gh` is checked for, and checked as signed in, before the push. When it is not, say so in the button's place rather than offering a route that fails after the push.

## Measured from where the branch started

A patch is measured from the commit the node's branch started at, not from wherever the repository is now. A run recorded before that was written down says so plainly (`unmeasured: true`) instead of showing an empty patch.

## Do and don't

- Do mark added and removed lines with + and − as well as colour.
- Don't show a patch for a decision; only commits are resolved against git.

## Accessibility

- The diff is a focusable `pre` named "The patch", so it scrolls by keyboard. Ligatures are off.
