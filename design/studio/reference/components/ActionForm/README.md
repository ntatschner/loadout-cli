# ActionForm

One thing a command does, in place on its page. It replaces the generic command drawer (ActionDrawer) wherever a page offered a command through a form built from its options.

## What it shows

- A **title** in plain words ("Clone it onto this machine"), and **why**: one line on what it does.
- The **fields people use**, each in plain words, with the option it fills shown small beside the label (`--destination`). An argument is just its plain label. Optional fields say so.
- **More options**: every other option the command takes, folded. Its summary counts them and says how many are set ("More options · 4, 1 set").
- **The line** it will run, updated as you type.
- **Preview**, when the command can try it first. With `--dry-run`, Preview adds it. For a command that only acts with an option (`--apply`, `--fix`, `--yes`), that option is never a checkbox: Preview runs without it, and the action adds it (`applyWith`). Preview changes nothing.
- **The action**, named for what it does ("Clone it", "Compress", "Forget it"). It is solid when it changes something and red when it can't be undone, and anything that can't be undone asks first, by name.
- A **session** command (one that asks as it goes or starts an agent) opens a terminal Studio hosts, labelled so.
- What it said, under it, and anything it returns drawn under that. `onRun` may return a promise: the button, or the asking Dialog, is busy (its `busy` word) until it settles, then says what happened; `false`, a throw or a rejection says "✕ why" (`failed` when there's no reason) and an asking Dialog stays open.

## Fields

- `fixed` fields are what the page already knows (this project, this idea). They are shown, and can't be changed.
- `required` fields stop the action until they are filled, and the button says what's missing.
- Every option the command has is offered: the main ones in place, the rest under More options. Nothing is only in the Commands page.

## Do and don't

- Do name the action for what it does, never "Run".
- Do keep the line visible: it is what the button runs, exactly.
- Don't use it for a list of things (ItemList), a value a command keeps (SettingRow), or a command that only reads (Readout).
