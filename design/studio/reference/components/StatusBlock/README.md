# StatusBlock

StatusBlock is a hosted session's status in full, for its Details: the context window and what fills it, what the session has cost, each of the plan's usage windows with when it resets, and every specialist it was given, by name and linked to what it says. Its only action is a command, shown whole, for bringing a line back to the terminal: nothing here types into the session. Ported from main's `studio/src/lib/components/StatusBlock.svelte` as built.

## What it shows

- **Status**, then when it was read ("Updated 18s ago."). Past twice the refresh interval it says, in Needs, "These figures are 7m old: the session has not redrawn its line since."
- **Context window used**: a Meter with its 70% mark, "of 200k tokens", and "filling" or "nearly full" past the mark.
- **What fills it**: In the window, Read from cache, Written to cache, Not cached, Last reply. Each only when Claude sent it.
- **Cost so far** (Claude's estimate) and **Lines** (+40 −7).
- **Five-hour window** and **Seven-day window** as Meters with when each resets, "getting close" past 70%.
- **Spend limit**: "$23 of $50 monthly · resets in 5d", or its share.
- Without limits: "No usage limits: Claude sends them for a subscription, or behind a gateway with a spend limit."
- **Specialists**, each a link to `#context/<project>/specialist/<id>` (plain text without a project).
- What the terminal shows, and the command that changes it: `loadout config set statusline-in-studio full` (or `off`).

## What you provide

- `status`: the session's `SessionStatus`; `now`: the time to measure against. `set({ status, now })` redraws.
- Place it in the session's Details column (about 300px), after Facts, while the session is live and has a status.

## Do and don't

- Do show figures as Claude sent them, and only those.
- Do keep the command whole and copyable. It changes a setting, so it is shown, not run.
- Don't add buttons that type into the session.

## Accessibility

- A `section` named "Status" with an `h2`; Specialists is an `h3`.
- The Meters are `role="meter"` with a value text that includes the over words.
- The figures are a `dl`, so each value is read with its name.
