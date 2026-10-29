# RunDepths

RunDepths is the lower half of a run's page: five tabs, reached with the keys 1 to 5 from anywhere on the page, or with the arrow keys once you are on the tabs. The classic dashboard has the first four; Studio adds the fifth, because a run that delivers documents is read by its files more than by its patch.

## The five depths

1. **What it said**, at two resolutions. The run's own journal, one line every few seconds with repeats counted (×6); or pick a node and see everything it did, every tool it called and everything it said, with sub-agents indented and marked. Under a live node's stream sits SayToNode. Both are drawn with main's Journal (stage 14's `Journal`, named "Journal" or "What implementer/1 did"); lines may be given as objects (`{ time, text, family, kind, sub, repeats }`) or as the older arrays.
2. **What it cost.** RunNumbers first, then every exchange one by one: round, node, model, messages, refused, cost, and what the report came back as.
3. **The papers.** The run's own summing-up first, then each node's brief beside its report, in the words the model saw. A brief you changed before it went out says so and shows the lead's original. Before the lead has briefed anyone: "No brief has gone out yet."
4. **What it changed.** Each node's Patch, file summary first, with Open a pull request. A node whose diff could not be read (`{ node, error }` among the patches) says why in its place, "reviewer/1: …", as main.
5. **What it delivered.** The Outbox: every file the run left behind, committed or not, in a tree beside a viewer that previews, shows the change and downloads.

## Where the data comes from

The daemon already serves each depth under the run: `/api/runs/<run>/events` (the journal, live), `/stream/<node>` (one node's stream), `/spent` (exchanges and numbers), `/documents` (the papers) and `/diff/<node>` (a patch). What it delivered is listed by `loadout team outbox <run> --json`; reading a file's contents needs the routes proposed under Outbox. Everything is redacted on the way out; anything that cannot be checked in time is not shown at all.

## Do and don't

- Do ignore 1 to 5 while someone types in a field, a terminal or a dialog.
- Do say plainly when a patch cannot be measured, rather than showing an empty one.
- Don't mine an agent's own transcript files for any of this.

## Accessibility

- Tabs with roving focus; each tab announces its key (`aria-keyshortcuts`). The journal is a focusable list so it scrolls by keyboard.
- The exchanges table keeps its headers; below 640px of its own width each row becomes a labelled stack, with no sideways scroll.
