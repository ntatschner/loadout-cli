# StatusStrip

StatusStrip is a hosted session's status, one row on the foot of its terminal, from what its status line last handed over: the model, the context, what it has cost, the plan's five-hour window and how many specialists it was given. Every figure is Claude's; one it did not send is left out rather than drawn as nought. Old figures say so, since Claude only redraws on its own events and its refresh interval. Ported from main's `studio/src/lib/components/StatusStrip.svelte` as built.

## What it shows

- **The model**, as Claude names it ("Opus 5.5").
- **ctx**: a short beam and the share of the context window in use. Past 70% it turns to Needs and says **filling**; past 90% to Bad and says **nearly full**.
- **cost**: Claude's estimate, in cents below ten dollars and whole dollars above.
- **5h**: the plan's five-hour window as a beam and a share, with when it resets as its title. Without one, the **spend** limit's share where Claude sent it.
- **How many specialists** it was given ("2 specialists").
- **How long ago** it was read, at the end ("just now", "40s ago", "3m ago"). Once the figures are older than twice the refresh interval it says **old: 7m ago** in Needs and the rest dims.

## What you provide

- `status`: the session's `SessionStatus`, as the daemon serves it on the session row.
- `now`: the time to measure ages and resets against (Studio's ticking clock). Call `set({ status, now })` when either changes.
- Put `el` in a `<div class="foot">` at the end of the Terminal, as main's Terminal `foot` snippet does; it is drawn only while the session is live and has a status.

## Do and don't

- Do leave a figure out when Claude did not send it. Never draw it as 0.
- Do keep the words beside the colour: "filling" and "nearly full" say what Needs and Bad say.
- Don't put actions here. The full picture and the one command are in the session's Details (StatusBlock).

## Accessibility

- `role="group"` named "Session status".
- A polite live region says **once** when the context moves from one level to another ("Context 78% used, filling", "Context back to 42%"), never on the first reading: the figures change every redraw, and reading each one out would drown the terminal.
- The beams are `aria-hidden`; the figure beside each says it in text.
