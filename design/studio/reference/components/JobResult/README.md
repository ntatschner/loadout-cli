# JobResult

JobResult is one command run from Studio: how it stands, the line it ran, and what it said once it ends. Main reads the job again whenever the job list changes, which is how the output arrives; here the page passes the job in and calls `update(job)` as it changes. It is used under a command's form (compact) and on the Commands page. Ported from main's `studio/src/lib/components/JobResult.svelte` as built.

## What it shows

- The command's **path** as a heading ("task list"), unless compact.
- Its state as a Signal (**running**, **done**, **failed**, **stopped** for a cancelled job), then "started 11:52 · took 1s · exit code 0".
- The **line** it ran, `$ loadout task list --project loadout-cli --json`, unless compact.
- While running: **Cancel** (danger, "stops it and anything it started") and "Running. What it says appears here when it ends."
- Once ended: **What it said**, pretty-printed when it is JSON, or "It said nothing on standard output."; **On standard error** in Bad; "It said more than Studio keeps; the end was left out." when truncated; and **Run it again** ("opens its form") when you pass `onagain`.
- A problem reading the job, as an alert above it.

## What you provide

- `job`: the job as `GET /api/v1/jobs/<id>` returns it; `update(job)` each time it is read again.
- `oncancel(job)`: cancel it (`DELETE /api/v1/jobs/<id>`); a rejected promise shows its message as the problem.
- `onagain(job)`: open its form again. `compact` under a form. `disabledReason` while Studio is only watching ("Studio is only watching: type the command in a terminal").

## Do and don't

- Do show what the command printed exactly, in its own box that scrolls inside itself.
- Do keep Cancel's words: it says what it stops, and it is Bad and a word, never colour alone.
- Don't draw a reading command's answer here. A command that only reads belongs in a Readout.

## Accessibility

- A `section` named "Result of <path>".
- "Running…" is `role="status"`, so the change is heard; a problem is `role="alert"`.
- Each output box is focusable and named ("What it said", "On standard error"), so a keyboard can scroll it.
