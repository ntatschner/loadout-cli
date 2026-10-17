# RunShape

RunShape draws a run the way the Map draws work: the lead's line runs through its rounds as stations, and the nodes each round briefed hang from that station as branches, each with its Signal. It answers "where has this run got to, and who did what in which round" at a glance.

## What you provide

- `rounds`: each with its state word (`done`, `round running`/`running`, `waiting for you`, `failed`), what the lead did in it in words, and its nodes as `[name, word]`.
- `next`: while the run goes on, what starts the next round ("starts when you answer"). Drawn dashed.
- `end`: once it has ended, its word and how ("The lead said done: 2 of 2 met", "Stopped: it spent its budget", "Stopped: two rounds without progress", "Stopped by you").
- `onPick` and `picked`: pressing a node shows its own stream under What it said.

## How it draws

- The line is the accent; stations are rings with the round's number. The current round glows in Moving, a waiting one has a Needs ring and halo, a failed one a square Bad edge. The state is also the word beside "Round 3".
- Below 620px of its own width it turns vertical: the line runs down the left, rounds stack, and branches indent.
- Line colours (`line-1`…`line-8`) are not used here: those are the Map's identity colours. A run's own line is the accent.

## Accessibility

- An ordered list of rounds; each round's nodes are a list named "Briefed in round 2". Station numbers repeat the list position and are hidden from screen readers.
