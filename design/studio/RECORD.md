# Record Studio's designs in Loadout, before any of them is built

Studio's six pages (`PAGES.md`) are built and shipped (by 0.85.1), and so is the field map (`BACKGROUND.md`, in 0.104.0). Four more briefs are designed and none is built: `ROUND4.md`, `OFFICE.md`, `CONTROLS.md` and `WORKFLOW.md`. This step writes Loadout's own records for them now, so that whoever picks any of them up later finds where it was designed and what was decided, and knows it isn't built yet.

**Records only: this builds nothing.**

- Run only these commands: `task list`, `task declare`, `idea add`, `memory show`, `memory write`, `memory replace` and `workspace save`. None of them starts anything. They write Loadout's records, and `workspace save` commits and pushes the workspace.
- Don't run `idea refine`, `idea dump add`, `team run`, `team schedule add` or `launch` (`loadout <project>` is `launch` too). Don't start a session or a build, don't change code, and don't open a branch or a pull request.
- Never change an item's state or title, and never declare anything `doing`. Open tasks are listed in every new session's context, so the "Designed, not built" sentence in each note is what tells a later session to leave the item alone until someone asks.
- Every command takes `--dry-run`. Run each once with it first, and check what it would write.

These records are claims with a name and a date, and the list this was written from wasn't checked against the workspace. Work from what the workspace says now, and change nothing you can't see.

Each brief's own last section, **Record it in Loadout**, updates these same records when that brief is built: it takes the "Designed, not built" sentence out and says where it was built.

## The sentence

Wherever a note below says **NOT BUILT**, write this sentence in its place:

> Designed, not built. Don't start on this from this record; build it only when someone asks, from its brief.

## 1. Read the list

Run `loadout task list --project loadout-cli --all --json`. Ideas are tasks of kind `idea`, so they are in it too.

## 2. Append a note to each item that exists

`task declare` **replaces** the note and **sets the state**. So re-declare each item with its **current state** and its **current note, then a blank line, then the note below**:

```
loadout task declare <id> <its current state> --note "<its current note>

<the note below>" --project loadout-cli --by "studio design"
```

- Skip an id that isn't there and tell me; don't create it.
- If an item's note already holds this note, leave it alone.

| Id | Note to append |
|---|---|
| `completely-seperate-dashboard-keeping-th` | Studio's designs and handoffs are in `design/studio/`. Built: `HANDOFF.md`, `NEXT.md` (0.68.0) `PAGES.md` (the six generic pages, in every mode, by 0.85.1) and `BACKGROUND.md` (the field map background and the nav's edge, 0.104.0). Designed, not built: `ROUND4.md` (run progress, Red Alert, tasks in Start a run, What it did), `OFFICE.md` (the office in Studio, with the classic dashboard's engine shared) `CONTROLS.md` (every command on Studio's pages as a control made for it, not a drawer) and `WORKFLOW.md` (what you came for, first, in every mode: live work first, one waiting count, a peek, layouts, confirmations). Don't start those from this record; build each only when someone asks, from its brief. The default switch itself is the check in CONTRIBUTING.md, "Before Studio becomes the default". |
| `team-run-visibility-and-reliability` | NOT BUILT Designed in `design/studio/ROUND4.md`, Part 1, which lists the server changes. It covers: progress line, criteria by round, a stall holds the run after three rounds with no criterion moving and asks you, a pick-up sheet asking for what it ran out of, and what it was refused and decided. Prototype: `#run-20261017-1105-e41a`. |
| `visual-full-page-red-alert` | NOT BUILT Designed in `design/studio/ROUND4.md`, Part 2: an edge glow and one line, off by default (`studio-red-alert`). Sound, notifications and a tab badge are out of scope. |
| `add-task-drop-down-in` | NOT BUILT Studio's launch sheet and the TUI already pick a task (#159). `design/studio/ROUND4.md`, Part 3, adds the picker to Start a run (`--task`), and Start a session on it and Run a team on it to task rows. The TUI's team-run form is not designed. |
| `measure-loadout-s-value-to` | NOT BUILT Designed in `design/studio/ROUND4.md`, Part 4: Spend → What it did, counted never estimated, plus the event log it needs (`<State>/events/events.jsonl`) and the read `loadout value`. |

## 3. Record the six pages as done

`PAGES.md` is built and shipped, so this one is recorded as done. If `studio-six-pages` isn't on the list:

```
loadout task declare studio-six-pages done --title "Rebuild Studio's six generic pages, in every mode" --note "Built and shipped by 0.85.1. Designed in design/studio/PAGES.md: Ideas, Projects (with Forget project), Context, Settings, Memory and Spend, each in its own shape (Part 1), and each page in Terminal Focus, Team Focus, Minimal and Pure Focus (Part 2, after the modes). Prototype and design system are linked from it." --project loadout-cli --by "studio design"
```

If it's there and not `done`, declare it `done` with its current note and, after a blank line, "Built and shipped by 0.85.1." This is the one state change this step makes, because the work is merged.

## 3b. Add the office as a task

If `studio-office` isn't on the list:

```
loadout task declare studio-office open --title "The office in Studio, with the classic dashboard's engine" --note "Designed, not built. Don't start on this from this record; build it only when someone asks, from its brief. Designed in design/studio/OFFICE.md: an Office place on Teams (Default, Team Focus, beside the terminals in Terminal Focus; not in Minimal or Pure), drawn by the classic dashboard's engine moved into a file both dashboards share, usable by mouse and keyboard in both, with Studio panels that show each action's command." --project loadout-cli --by "studio design"
```

If it's there already, treat it like the items in step 2.

## 3c. Add the controls as a task

If `studio-controls` isn't on the list:

```
loadout task declare studio-controls open --title "Studio's commands as controls, not drawers" --note "Designed, not built. Don't start on this from this record; build it only when someone asks, from its brief. Designed in design/studio/CONTROLS.md: every drawer on Studio's pages (105 CommandActions) replaced by a control made for its command (ActionForm, ItemList, SettingRow, Readout, ActionRow), reads shown as views from --json, common fields in place and the rest under More options, the Commands page kept as the catalogue, team webhook enable made terminal only, and the task board's reserved command line." --project loadout-cli --by "studio design"
```

If it's there already, treat it like the items in step 2.

## 3d. Add the workflow as a task

If `studio-workflow` isn't on the list:

```
loadout task declare studio-workflow open --title "Studio: what you came for, first, in every mode" --note "Designed, not built. Don't start on this from this record; build it only when someone asks, from its brief. Designed in design/studio/WORKFLOW.md: live work first everywhere, one waiting count, a peek over any session, run, idea or task (with a read-only session tail), jumps that land in the mode you're in, Edit layout on every page and mode kept as studio-layouts, Team Focus showing everything that needs you, and confirmations that show they are working and then close." --project loadout-cli --by "studio design"
```

If it's there already, treat it like the items in step 2.

## 3e. Make each one ready to build

So each task can be started from Loadout when someone asks, give it its build instructions (`--detail`, which `task show` prints) and its finish line (`--done-when`, which a team run is held to). This changes neither its state, title nor note: re-declare it with its **current state** and only these two options. `--done-when` replaces the task's conditions, so if a task already has some, show them to me and ask before replacing them.

- Skip an id that isn't there and tell me.
- Never queue these for a team or start them: this only writes the records.

**`studio-workflow`**:

```
loadout task declare studio-workflow <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build design/studio/WORKFLOW.md, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in design/studio/WORKFLOW.md is built as it says: the shared pieces, the server's read-only tail and attach, and every page and mode in its Page by page." --done-when "The checks in WORKFLOW.md's Before you finish pass." --project loadout-cli --by "studio design"
```

**`studio-controls`**:

```
loadout task declare studio-controls <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build design/studio/CONTROLS.md, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in design/studio/CONTROLS.md is built as it says: its five components, every page's drawers replaced page by page, the task board fix and studio-commands." --done-when "The checks in CONTROLS.md's Before you finish pass." --project loadout-cli --by "studio design"
```

**`studio-office`**:

```
loadout task declare studio-office <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build design/studio/OFFICE.md, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in design/studio/OFFICE.md is built as it says: the shared engine, keyboard use in it, and Studio's Office view and panels." --done-when "The checks in OFFICE.md's Before you finish pass." --project loadout-cli --by "studio design"
```

**`team-run-visibility-and-reliability`** (ROUND4.md, Part 1):

```
loadout task declare team-run-visibility-and-reliability <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build Part 1 of design/studio/ROUND4.md, which is built with its other parts, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in Part 1 of design/studio/ROUND4.md is built as it says, server and Studio." --done-when "The checks in ROUND4.md's Before you finish pass." --project loadout-cli --by "studio design"
```

**`visual-full-page-red-alert`** (ROUND4.md, Part 2):

```
loadout task declare visual-full-page-red-alert <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build Part 2 of design/studio/ROUND4.md, which is built with its other parts, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in Part 2 of design/studio/ROUND4.md is built as it says." --done-when "The checks in ROUND4.md's Before you finish pass." --project loadout-cli --by "studio design"
```

**`add-task-drop-down-in`** (ROUND4.md, Part 3):

```
loadout task declare add-task-drop-down-in <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build Part 3 of design/studio/ROUND4.md, which is built with its other parts, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in Part 3 of design/studio/ROUND4.md is built as it says." --done-when "The checks in ROUND4.md's Before you finish pass." --project loadout-cli --by "studio design"
```

**`measure-loadout-s-value-to`** (ROUND4.md, Part 4):

```
loadout task declare measure-loadout-s-value-to <its current state> --detail "To build, only when someone asks: check out claude/determined-meitner-6wqi6z, branch off it as the brief says, and merge origin/main into your branch. Then build Part 4 of design/studio/ROUND4.md, which is built with its other parts, all of it, as one piece of work. Read it to the end first, then the files its Source of truth lists. Where the brief and the reference disagree, the brief wins; where something can't be built as written, stop and ask. Follow its Before you finish, commit in the steps it lists with no AI or co-author attribution lines, do its Record it in Loadout last, and don't push or open a pull request until asked." --done-when "Everything in Part 4 of design/studio/ROUND4.md is built as it says, with the event log and loadout value." --done-when "The checks in ROUND4.md's Before you finish pass." --project loadout-cli --by "studio design"
```
## 4. The background is built

`BACKGROUND.md` shipped in 0.104.0, and its own last step, **Record it in Loadout**, updated the idea `studio-embossed-background` when it was built. Don't add that idea or change it here. If it is on the list still carrying "Designed, not built", re-declare it with its current state and note, taking that sentence out and adding "Built and shipped in 0.104.0.", and tell me.

## 5. One memory topic

`memory write` replaces a topic's facts rather than adding to them.

- If `studio-design` doesn't exist:

  ```
  loadout memory write studio-design --description "Where Studio's designs and handoffs live" --fact "Studio's design system, prototype and handoffs are in design/studio/: HANDOFF.md (built), NEXT.md (built, 0.68.0), PAGES.md (six pages and their modes; built, 0.85.1), ROUND4.md (run progress, Red Alert, tasks in Start a run, What it did; not built), BACKGROUND.md (the field map background and the nav's edge; built, 0.104.0), OFFICE.md (the office in Studio; not built), CONTROLS.md (commands as controls, not drawers; not built), WORKFLOW.md (what you came for, first, in every mode; not built). A brief marked not built is for later: don't start it unless asked. Each brief names what is decided; don't reopen those decisions." --project loadout-cli
  ```

- If it exists, or the write refuses because a similar topic exists, add that fact to the topic instead:
  1. `loadout memory show <topic> --project loadout-cli`
  2. edit the text;
  3. `loadout memory replace <topic> --project loadout-cli --file <the edited text> --expect <the version show printed>`.

  Tell me which topic.

## 6. Save the workspace

`loadout workspace save`, so your other machines have it.

## Then tell me

- what each step did: updated, created, or skipped and why;
- that nothing was started: no session, run, refine round, schedule or build.
