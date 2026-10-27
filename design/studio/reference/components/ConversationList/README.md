# ConversationList

ConversationList shows past conversations from both agents, each with what it was launched for and whether it is running, and the way to carry it on. It is shared by Sessions, which owns the history (`loadout sessions`), and a project's page, which shows its own five most recent. Ported from main's `studio/src/lib/components/ConversationList.svelte` as built.

## What it shows

- Each conversation's **title**, then where and when: "starstats · claude · cache-leaderboard · 4 min ago" ("not a project · /home/nina/scratch/importer" for a folder that is not one; no project on a project's page).
- What would carry over: "carries over task ‘Cache the leaderboard query’, mode build", "launched with nothing to carry over", or "nothing carried over: no launch on record"; then "open in Studio" or "looks to be running elsewhere".
- **Open** ("goes to its terminal") for one already in a Studio terminal.
- Otherwise **Resume** (or **Register and resume** for a folder that is not a registered project) with `$ loadout resume <id> --project <slug>`, and **Resume with changes…** on a registered one.
- Held, Resume says why: "Studio is only watching: type the command in a terminal", "This daemon cannot open a terminal here", or "one at a time" while another starts.
- With none: "No conversation from either agent is on record here yet."

## What you provide

- `conversations` and `label`; `inProject` on a project's page; `set(…)` as the history changes.
- `onresume(conversation)`: resume it and resolve to the new session's id (main asks first when it looks to be running elsewhere, or its folder must be registered); `onopen(session, launched)` to go to a terminal; `onchanges(conversation)` to open the launch sheet.
- `watchOnly`, `startsSessions`, and `now` for "4 min ago".

## Do and don't

- Do go to a conversation already open in Studio, never start it twice.
- Do say what carries over before Resume. Nothing is guessed where there is no launch on record.
- Don't hide Resume when it can't be used: it says why.

## Accessibility

- A list named by `label`. Each row's actions are real buttons; a held Resume is `aria-disabled` with the reason written under it.
- Below 600px the actions drop under the words, so nothing scrolls sideways.
