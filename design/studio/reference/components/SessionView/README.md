# SessionView

SessionView is the page for one session: a slim header with its title and facts, the Terminal filling the rest of the page, and a side panel of details that folds away to give the terminal the full width.

## What you provide

- `line`: title, project, agent, mode and how long it has run.
- `terminal`: the Terminal's options, including its state and session id.
- `facts` (main's: Project, Agent, Mode, Task, Running, Quiet, Memory, Windows, Drawn with), `loaded` (what "This session would load" said at launch), `events` (the last few, newest first) and `actions` (HaloButtons with what they do).
- `status` (and `now`): the session's status, drawn by StatusBlock after Facts, where main puts it; `statusBlock.set()` takes new figures. Put the StatusStrip on the terminal's foot with `terminal.foot`.
- `commands` and `note` for the rest of Actions, before its buttons, as main lays it: "Started with:", "Carry this conversation on in a terminal instead:" (`resumeCommand(id, project)`), then "Sessions survive a reload of this page, not a restart of the daemon."
- `sections` for anything else in the panel: a node, anything with `.el`, or `{ title, content }`, at "facts" (after Facts, the default), "loaded", "events" or "actions".
- `button` for one in the header before Details: main's "Type here instead" ("another window holds the input · takes it over") when another window holds the input. `setButton(null)` takes it away.
- `details: false` to start with the panel folded.

## Layout

- Desktop: the terminal fills the page beside a 300px panel. "Hide details" folds the panel; the terminal takes the width.
- Below 960px the panel sits under the terminal, folded by default.
- On a phone the terminal fits 80 columns to the width (see Terminal), and the bar under the screen is the way to see what the session needs.

## Do and don't

- Do keep the page's own controls out of the terminal's way: Ctrl+] leaves the terminal for the details toggle, never for an action such as End the session, where a stray Enter would do harm.
- Do say what ending a session does. It stops the agent in this terminal; there is no `loadout` command for it, so say that instead of a command.
- Don't open more than one terminal on the page.

## Accessibility

- The header holds the page's only `h1`. The panel is an `aside` named "About this session", and its toggle has `aria-expanded` and `aria-controls`.
- Everything in the panel is text, and none of it is only in the terminal.
