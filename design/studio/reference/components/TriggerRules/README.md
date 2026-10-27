# TriggerRules

TriggerRules is the Teams page's **Trigger rules** place: each rule on this machine with where it stands against its caps, a form that writes and changes them, the rules teams suggest, who is trusted beyond the collaborators, and what the rules decided lately. Everything here is read with a `loadout trigger …` command and done with one; the form shows the line it will run, and Preview runs it with `--dry-run` first. Ported from main's `studio/src/lib/components/TriggerRules.svelte` as built.

## What it shows

- **Trigger rules**, with **Add a rule** (`loadout trigger add …`). Each rule: its id, `v3 · on pr-opened · starts reviewer (supervised)` (or `daily at 02:00`, `every 1h`, `runs docs audit`), tags for *paused at a cap*, *disabled* and *the team changed its suggestion*, its goal, what it watches (`Watches website · base = main`), its standing (`2 of 10 runs · $3.50 spent of $12.00 · $4.00 set aside`) and, when it was not acted on, `✕ Not acted on: …` as an alert. Its buttons: **Change** (`loadout trigger add <id> …`), **Try it** (`loadout trigger test <id>`, whose output opens in a dialog), **Resume** when paused (`loadout trigger resume <id>`, Needs), **Remove** (`loadout trigger remove <id>`, Danger, confirmed in a danger dialog that says what is withdrawn).
- **Suggested by teams**, only when there are any: `team · name`, what it waits for, *accepted as …* or *changed since you accepted it*, and **Accept it** / **Take the change** (`loadout trigger accept <team> <name>`).
- **Trusted beyond the collaborators**: each login with its scope and *now called …* when it was renamed, **Stop trusting** (`loadout trigger disallow <login>`), and a form: GitHub login, Where (Everywhere, One repository `--repository owner/name`, One owner `--owner name`), **Trust them** (`loadout trigger allow <login> [--repository …|--owner …]`).
- **What the rules decided lately**: the last 25 journal entries, newest first, from `loadout trigger journal --limit 25`.
- **The form** (a sheet dialog): its name (read-only when changing), when this happens (the event kinds and the fields each carries), the clock's interval or time of day, a team (goal, posture, spend cap, each run's budget) or a built-in, which projects count, conditions on the event's fields, runs at most in the window, the window, a debounce and *Record it without letting it act yet* (`--disabled`). Under it the exact `$ loadout trigger add …` line, then **Preview** (runs it with `--dry-run`), **Save the rule** / **Save the change**, and what the command said.

## What you provide

`teams`, `projects`, `disabledReason`, `onfail`, as main's Props. In place of main's `readJson` and `runJob`, `read(path, words)` and `run(path, words)`: the component calls them with the same paths and words main does. `host` and `contained` say where its dialogs open. It returns `{ el, load, add, change, set }`.

## Do and don't

- Do show the line before it is run: the form's command follows every field, and the reason it cannot be saved yet (`give the rule a name first`, `a clock rule that starts a team names the one project it runs on`…) stands in place of Preview's and Save's commands.
- Do keep the danger in words: Remove and Stop trusting say what they do, and removing is confirmed.
- Don't decide here what the command decides: the form's checks only say early what `trigger add` would say anyway.
- As built, the *paused at a cap* tag and the *Not acted on* line use main's `var(--ls-needs, inherit)`, which no token defines, so they are drawn in the ink around them; the words carry the meaning.

## Accessibility

- Each section is labelled by its heading. The problem line is `role="alert"`; what Preview or Save said is `role="status"`.
- The form, the confirmation and Try it's output are dialogs that hold focus and return it; Remove's is an alertdialog with its danger action last-focused, as main's.
- Every field has a visible label; pickers, mode pickers and switches keep focus when the form redraws around them.
- A disabled button says why in place of its command.
