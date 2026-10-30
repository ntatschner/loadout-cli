# Urgency

Urgency decides which one thing in a list most needs you, raises it, and lets everything else recede, so the eye lands in the right place without reading the list.

## What you provide

- The items, each with `waiting` or `problem` and `askedAt`. `rankUrgent(items)` orders them as `liveFirst` does: what waits on you before a problem, the oldest first within each.
- The rows, and the index of the one to raise: `applyUrgency(rows, index, "needs" | "problem")`. Pass `-1` when nothing needs you.

## Do and don't

- Do raise one thing only. The rest of the waiting items stay listed, in order, with their own signals.
- Do keep the order of the list stable; rising is a shift and a light, not a move to the top.
- Receding fades to `recede` (0.72) in Dark only, where text stays 4.5:1 or better. In Light and both high-contrast themes a fade would break contrast, so nothing fades and the rise alone carries it.

## Accessibility

- The rise repeats what the row's Signal already says; it adds no meaning a screen reader would miss.
- Under no motion the row does not shift sideways; the edge and light stay.

## Live work first

`liveFirst(items, opts)` is the one order for every list of work (WORKFLOW.md's Decisions): **waiting on you** (a gate or question, a trigger approval, a session waiting or asking for approval, an idea with questions open), **running**, **you might pick it up** (failed, blocked or ended short of its done-when, ended when the daemon stopped, a round that failed, a branch waiting to land), then **history** (ended, done, past conversations). The oldest wait comes first within each live group; history keeps the order it was given. Items carry a `state` word (or `word`) and `since`, `at` or `askedAt`; `opts.state`, `opts.since` and `opts.group` read them otherwise, and `opts.only` keeps some groups. It returns a new array. `liveGroup(item)` names an item's group, and `LIVE_GROUPS` their names.

`waitingOf({ lines, runs, approvals, ideas })` is the one count of what is waiting on you, the same in the mode bar, the rail, Home, Teams, Minimal and Pure: lines and runs that wait on you, every trigger approval, and ideas with a question open. It returns `{ count, list }`, the list oldest first, each `{ kind, id, title, since, item }`; one id counts once. Say it as "N waiting on you".
