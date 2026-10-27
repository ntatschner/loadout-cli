# Record Studio's designs in Loadout, before any of them is built

Studio's six pages (`PAGES.md`) are built and shipped (by 0.85.1). Its next four briefs are designed and none is built: `ROUND4.md`, `BACKGROUND.md`, `OFFICE.md` and `CONTROLS.md`. This step writes Loadout's own records for them now, so that whoever picks any of them up later finds where it was designed and what was decided, and knows it isn't built yet.

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
| `completely-seperate-dashboard-keeping-th` | Studio's designs and handoffs are in `design/studio/`. Built: `HANDOFF.md`, `NEXT.md` (0.68.0) and `PAGES.md` (the six generic pages, in every mode, by 0.85.1). Designed, not built: `ROUND4.md` (run progress, Red Alert, tasks in Start a run, What it did), `BACKGROUND.md` (the field map background and the nav's edge), `OFFICE.md` (the office in Studio, with the classic dashboard's engine shared) and `CONTROLS.md` (every command on Studio's pages as a control made for it, not a drawer). Don't start those from this record; build each only when someone asks, from its brief. The default switch itself is the check in CONTRIBUTING.md, "Before Studio becomes the default". |
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

## 4. Add the background as an idea

If `studio-embossed-background` isn't on the list:

```
loadout idea add --id studio-embossed-background --text="Designed, not built. Don't start on this from this record; build it only when someone asks, from its brief. A military-style embossed background for Studio, animated with a static variant, and the same treatment for the nav. Designed in design/studio/BACKGROUND.md: a field map, plain / map / map-animated as studio-backdrop, the scan only under full motion and never in Pure Focus, plain in high contrast." --project loadout-cli --by "studio design"
```

`idea add` only records the idea. Don't run `idea refine` on it.

If it's there already, treat it like the items in step 2.

## 5. One memory topic

`memory write` replaces a topic's facts rather than adding to them.

- If `studio-design` doesn't exist:

  ```
  loadout memory write studio-design --description "Where Studio's designs and handoffs live" --fact "Studio's design system, prototype and handoffs are in design/studio/: HANDOFF.md (built), NEXT.md (built, 0.68.0), PAGES.md (six pages and their modes; built, 0.85.1), ROUND4.md (run progress, Red Alert, tasks in Start a run, What it did; not built), BACKGROUND.md (the field map background and the nav's edge; not built), OFFICE.md (the office in Studio; not built), CONTROLS.md (commands as controls, not drawers; not built). A brief marked not built is for later: don't start it unless asked. Each brief names what is decided; don't reopen those decisions." --project loadout-cli
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
