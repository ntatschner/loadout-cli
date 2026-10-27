# AskPane

AskPane is the palette's conversation with Loadout: type `?` and a request in plain words in the CommandPalette and it takes the place of the results, each request and its answer, newest last. An answer says who gave it; a near miss can be asked as it stands; a plan is a card to approve. It is answered by `loadout ask` through the daemon, and the conversation lasts until somebody clears it, surviving the palette closing and opening again. Ported from main's `studio/src/lib/components/AskPane.svelte` as built.

## What it shows

- Before anything is asked: "Ask in plain words, then press Enter. Loadout answers what it can from its own records: what you spent, which tasks are easy or need work first, or what to queue and start."
- Each request as **? what did I spend this week**, then "Asking…", or why nothing came back (in Needs), or the answer:
  - **Answered by Loadout**, or **Answered by the agent, for $0.37**;
  - the summary;
  - spend **by model**: model, tokens, and dollars or "no price", with any caveat in Needs;
  - tasks in order, each "title  t-14 · starstats" (or "workspace") with why it was picked;
  - a plan, as an **AskPlanCard**;
  - near misses, as options to ask as they stand, and **Ask the agent instead, for about $0.40**, with its basis and "It only reads; anything it proposes comes back as a plan."
- **Clear the conversation**, once there is one.

## What you provide

- `turns` and `asking`, and `set({ turns, asking })` as they change. Or let the pane keep the conversation itself: `ask(query, asker)` adds the request at once and its reply when `asker` settles (main's `AskConversation`), and `clear()`.
- `onrewrite(rewrite)` to ask a near miss; `onagent(query)` to ask the agent (a job, `loadout ask --use-agent --query=… --json`, carrying on its conversation with `--resume`); `approve(plan, team, cap)` for the plan cards; `onclear()`.

## Do and don't

- Do say who answered and what the agent cost. The agent's cost is never part of a plan's cap.
- Do keep the agent a choice: it is offered with its rough cost, never asked on its own.
- Don't start anything from an answer. Anything that would start something comes back as a plan, carried out only when approved.

## Accessibility

- The pane is `aria-live="polite"`; a turn already shown keeps its nodes, so only what is new is spoken.
- A failure is `role="alert"`. The list of models is named "By model", the near misses "Closest requests Loadout can answer".
- The `?` and `✦` glyphs are `aria-hidden`; every option is a real button. Ask the agent and Clear are disabled while a request is out.
