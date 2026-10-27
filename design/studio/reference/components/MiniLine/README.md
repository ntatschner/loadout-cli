# MiniLine

MiniLine is a run's rounds as a line small enough for a board row: a stop for each round with a tick per node, the one running lit, and a dashed stop for what comes next or a solid one where it ended. It is decoration: the row beside it says the same in words. Main draws it in each row of RunBoard (Team Focus). Ported from main's `studio/src/lib/components/MiniLine.svelte` as built.

## What it shows

- **A stop per round**, joined by the accent line. A round that is running breathes in Moving and the segment into it carries a travelling light; one waiting for you has a Needs ring and wash; a failed one is a square in Bad.
- **A tick per node** under each stop, in its Signal's family (a square for Bad).
- **The end**: a dashed segment and an empty stop while it runs, or a Good stop where it ended.

## What you provide

- `rounds`: each round's word and its nodes as `[name, word]`, from the run's read.
- `running` while it is live (moving or waiting for you), otherwise `end`.
- `set(patch)` as rounds come in.

## Do and don't

- Do put the run's goal, team, round and Signal beside it, in words. Main's board row does.
- Don't make it the only place a state is said: it is hidden from assistive technology.
- Don't use it for a run's page: that has RunShape, which shows the nodes by name.

## Accessibility

- `aria-hidden="true"`: the row's words carry everything. Each node tick has a `title` for a pointer.
- Shapes carry meaning with colour: square for Bad, a ring and wash for Needs, a dashed segment for what comes next.
- The breathing and travelling light stop under reduced motion, none, Pure mode and `prefers-reduced-motion`.
