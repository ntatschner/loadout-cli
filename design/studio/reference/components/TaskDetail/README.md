# TaskDetail

TaskDetail is one task in full, opened from its card: the detail a list leaves out, the note, and every change of state with who said so and when. It is read with `loadout task show`, so it says what a terminal would. The card already has the title and the moves; this is for reading. It opens from a TaskBoard card and from a Projects address. Ported from main's `studio/src/lib/components/TaskDetail.svelte` as built.

## What it shows

- A Dialog titled with the task's title (or its id until it is read).
- Chips: its state as a Signal, its id, and its list (a project, or "the workspace-wide list").
- "Reading…" until it is read, or why it could not be (in Bad, with the words).
- When it came from an idea: "From an idea accepted Oct 24, 04:40 PM: the plan is at `ideas/starstats/t-14/plan.md`. Open the idea".
- **Note**, **Detail** (as Markdown) and **History**: each change as its state, who said so ("nobody named" when nobody was) and when, with what else changed under it. A task recorded before there was a history says "blocked, said by rowan on Sep 2, 11:05 AM. Nothing earlier was kept."
- "The same as `$ loadout task show t-14 --project starstats`." and **Close**.

## What you provide

- `task` as the card has it, and `onclose`.
- `read(path, words)`: read `task show` with those words and resolve to its `--json`; or pass `shown` (already read) or `problem`. `set({ read | shown | problem })` later.
- `onidea(href)` for Open the idea; `onsignedout` for a read refused as signed out. `host`, `contained` and `behind` go to the Dialog.

## Do and don't

- Do say what a terminal would: the same command, the same history.
- Don't put the moves here. The card has them; this is for reading.

## Accessibility

- A modal Dialog named by its title; focus starts on Close, is held inside, and returns to the card. Escape and the dim close it.
- "Reading…" is `role="status"`; a problem is `role="alert"`.
- Note, Detail and History are sections named by their headings; History is an ordered list with `time` elements.
