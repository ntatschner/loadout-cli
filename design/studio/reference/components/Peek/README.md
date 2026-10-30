# Peek

Peek is one overlay over any piece of work: a session, a team run, an idea, a task, or one of a run's crew nodes. It shows the thing's live state and its main actions, so you can see it and act on it without leaving the page or changing mode. Point at a row and it opens beside it after a short delay; focus the row and it opens at once; press the row's peek button and it is pinned, a non-modal dialog that stays until Escape, its Close button or the peek button again. A click away closes only one that is not pinned. Escape closes it before anything else that claims Escape, but never while you are typing in a terminal. Below 600px wide it is a sheet from the foot of the screen. The same panel is also the mode bar's waiting list, a short block under the row in Minimal, and the view pane's preview in Pure Focus. This is new design, from `design/studio/WORKFLOW.md` (Decisions: "The peek"; Work: "The Peek component"). Main has no peek yet.

## What it shows

Every peek has a head with what kind of thing it is ("session", "team run", "idea", "task", "crew node"), its name, and its state as a Signal: word, glyph, colour and motion. Its left edge is in the state's family colour. Pinned, it also has an accent line along the top and a **Close** button, so a pinned peek is told apart by a word and not only by a line. Under the head is one line of facts joined by middle dots.

- **A session**: its last lines, read-only, in the terminal's own colours, from the daemon's tail (`GET /api/v1/sessions/{id}/tail`), with when they were read. Under them is its StatusStrip. If it is waiting, a Needs block says what for ("approval to run npm test"), and that it is answered in its terminal, because Studio never answers for an agent. Then **Type here** (solid and Needs when it is waiting), **Open beside** (Default mode), **End session…** (danger, asks first) and **Open**.
- **A run**: its team, project, round and time; its MiniLine; the node working now and what it is doing; its question as a compact Gate with that question's own answers; its Spend line; then **Hold after this round** (or **Let it carry on** when it is held), **Stop after this round** (danger) and **Open**. A finished run shows no Hold or Stop.
- **An idea**: its IdeaStages in their short form (the dots and "Stage 2 of 5: Answering"); the first question nobody has answered yet, in QuestionSet's one-question style (options as large chips, the recommended one marked, or your own words) with one **Answer** button; then **Refine** and **Open**. Once an answer goes, the next open question takes its place.
- **A task**: its state, its note (in Bad on a blocked one), **Start** (solid; `$ loadout launch <project> --task-id <id>`), **History** (`$ loadout task show <id>`) and **Open**.
- **A crew node**: its name, role, model, run and turns; the branch it works on; **What it did last**, its last journal lines with their times, read through the page's `journal(item)` the way a session's lines are read through `tail`; a Needs line when it has been quiet for three times its usual turn; then **Say to it**, which is SayToNode as it is everywhere else. Locked, it asks for the attach passphrase, swapped for a 30-minute grant. Unlocked, it is a short box and **Send**, with main's `$ loadout team say <run> --node <node> --message "…"` updating as you type. A node that has finished says to tell the lead instead. Last comes **Open**, which opens its run at that node.
- **The waiting list** (`list(items, anchor)`): "4 waiting on you", then every waiting item, oldest first, each with its kind, its name, how long it has waited, its Signal and its main action. A session has Type here and a trigger approval has Approve. A run or an idea has Answer, which opens its own peek in the same panel, with "← Waiting on you (4)" to go back.

Every action is a HaloButton with its exact command, and follows `data-commands`: on hover, always or never. An action that only goes somewhere says what it does instead ("opens it in full, in the mode you are in · changes nothing"). While an action is going, its button says so ("holding…"), carries `aria-busy`, and every button in the peek is inactive. Then one line says **✓** what happened, with the command, or **✕** and why. Nothing claims a success it did not get: a callback that returns `false`, throws or rejects has failed.

## What you provide

- `Peek(o)` once per surface, with the callbacks the page has. A button is shown only when its callback is given: `onOpen`, `onTypeHere`, `onOpenBeside`, `onEnd`, `onAnswer` (the run's gate), `onHold`, `onCarryOn`, `onStop`, `onAnswerIdea`, `onRefine`, `onStart`, `onHistory`, `onSay` and `onUnlock` (a crew node), `onApprove` (the waiting list). Each takes the item. One that changes something may return a promise; a string it gives back is used as the words of its success.
- `tail(item)`: the page's call to the daemon's read-only tail. It is read when the peek opens and every `tailEvery` ms (2000 by default) while it stays open. It never attaches, and never takes the input. `journal(item)` does the same for a crew node: its last journal lines, as `[time, text, family?]` or `{ at, text, family }`.
- `attach(row, () => item)` for each row. The row's peek button is the element marked `[data-peek]` inside it, or one you pass as `button`, or one Peek adds at the row's end. `attach` returns `detach`.
- `set(item)` when the open item is read again. It is drawn again in place, keeping what was typed, and a change of state is said politely. Use `setList(items)` for the waiting list.
- Items in the shapes `PeekSession`, `PeekRun`, `PeekIdea`, `PeekTask`, `PeekNode` (its `id` is `<run>/<node>`, with `say.state` as SayToNode's) and `PeekApproval` (see `index.d.ts`). `since` is when it started waiting on you, which puts the oldest first.
- `mode: "inline"` for Minimal and `mode: "preview"` with a `container` for Pure. `framed: true` (or `"sheet"`) draws a preview exactly as it floats, for a page that shows the peek itself, like this one.

## Where it goes

These are the surfaces WORKFLOW.md names:

- **The mode bar**: its "waiting on you" count opens the waiting list. Pressing it again, or Escape, closes it.
- **Home (Default)**: Needs you, Running and Pick up rows, and the Map's lines.
- **Sessions**: every live row peeks its last lines, with Type here, Open beside and End session.
- **A session**: "Next waiting →" peeks the next waiting session before switching.
- **The deck (Terminal Focus)**: a tab that is not in a pane peeks its last lines. A tab in a pane needs no peek.
- **Ideas**: the shelf's rows.
- **Teams**: Needs you, Running and Pick up rows.
- **The board (Team Focus)**: rows peek the round and the node working now.
- **A run**: crew nodes (Who is on it, RunShape's nodes) peek what they did last, with Say to it, as the crew node body.
- **Minimal**: the peek is the short block under the row (`mode: "inline"`), before the frame opens.
- **Pure Focus**: the view pane's preview as J and K move through the list (`mode: "preview"`), without Enter.
- **Projects**: a dossier's "Live here" rows.
- **Spend**: Running now rows peek, and open their session.
- **The rail**: an item with a need dot peeks its waiting items, as the waiting list.
- **The palette**: the highlighted In flight row peeks beside the palette.
- **Toasts**: a toast about a waiting thing peeks on hover.

## Do and don't

- Do give every row the same peek for the same thing, whatever the page or mode. A session peeks the same way everywhere.
- Do keep Say to it behind its passphrase, as SayToNode is everywhere else. The peek doesn't loosen it.
- Do pass the tail from the daemon's read-only tail. Never attach the session's input to fill a peek.
- Do let Open go to the full view in the mode you are in. A peek never changes mode.
- Don't put an agent's choices on buttons. A session's question is answered in its terminal; the peek only says what it is waiting for.
- Don't let a peek open over a terminal you are typing in, and don't take Escape from one: Peek checks both, so don't work around it.
- Don't open more than one. Each surface has one Peek, and opening another row moves it.

## Accessibility

- Pinned, it is `role="dialog"` with `aria-modal="false"`, named by the item's heading. Unpinned, it is a group the row points at. The waiting list is a dialog named by its count. In Minimal and Pure it is a region.
- From a focused row it is reachable. The row's link has `aria-describedby` pointing at a short gist ("session · waiting for you · approval to run npm test. The peek button keeps it open.") and `aria-controls` pointing at the panel. The peek button has `aria-expanded`, `aria-controls` and `aria-haspopup="dialog"`.
- When it is pinned, Tab from the peek button moves into it. Shift+Tab off its first control goes back to the button. Tab off its last control closes it and goes on to what follows the button.
- Escape closes it first and stops there (capture phase, propagation stopped), then gives focus back to the peek button. It is left alone while focus is in `.xterm`, a Studio terminal's screen or `[data-terminal]`, and while a modal dialog is over it.
- Focus from a press is not treated as a keyboard visit, so a sheet opened on pointer-down never covers the button being pressed.
- A change of state while it is open is said in a polite live region. The last lines are not read out as they change. Results are `role="status"`.
- Colour never carries meaning alone: every state is a Signal with its word, danger actions are in the Bad colour and say what they do ("Stop after this round"), and pinned shows a Close button. Text uses the system's tokens: AA in Dark and Light, AAA in both high-contrast themes. The peek button is at least 24px.
- Motion is a 180ms fade with a 4px slide (the sheet rises 16px), under full motion only. Under reduced or no motion, and in Pure Focus, it just appears.
