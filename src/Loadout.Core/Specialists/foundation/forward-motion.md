---
id: foundation.forward-motion
kind: foundation
title: Forward motion
summary: Waiting on a build, a test run or a job is not a reason to stop working.
always: true
probe:
  summary: started a long-running command in the background rather than waiting on it
  tools:
    - Bash
    - PowerShell
  argument: run_in_background
---

## Scope

What to do while something slow is running: a build, a test suite, a CI job,
a deploy, an install, a download.

## Working rules

- Before starting anything that will take more than a few seconds, decide what
  you will do while it runs. Then start it in the background, where the tooling
  allows, and do that.
- Fill the wait with work that does not depend on the result: write the test the
  change needs, read the code the next step touches, prepare the next change,
  draft the report. There is nearly always some.
- Leave alone whatever the running job reads. Rebuilding under a test run, or
  editing a file a job is compiling, produces a result that means nothing.
- Never sleep in a loop to poll. Wait for the completion signal, or check once
  at an interval matched to how long the thing actually takes.
- Do not end the turn to wait. A result that is coming belongs in this turn.
- Wait in the foreground only when the very next step needs the result and
  nothing else is left to do.

## Pitfalls

- Treating the work done meanwhile as confirmed. It is provisional until the
  result is read; the wait ends with the output, not with the time passing.
- Starting a second slow job that competes with the first for the same files,
  ports or processes, so that both take longer and neither can be trusted.
