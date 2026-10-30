# Prompts for a local agent

Paste one prompt per session, in a clone of `ntatschner/loadout-cli` with the Loadout CLI on the PATH. Run the first one once, to register the work as Loadout tasks. After that, start each build from its task (below) or paste its build prompt, in any order, one session each: none of the briefs depends on another, and each brief's **Order** paragraph says how to fit in with any that is already built.

Every brief branches off `claude/determined-meitner-6wqi6z`, where the designs live, and ends with **Record it in Loadout**. No brief pushes or opens a pull request until you say so.

## 1. Register them as Loadout tasks (once, first)

Give this to Loadout (a session it launches, or any agent with the Loadout CLI). It declares each unbuilt brief as a task, marked "Designed, not built", with its build instructions (`--detail`) and its finish line (`--done-when`), and starts nothing.

```
Check out claude/determined-meitner-6wqi6z and pull it. Then do design/studio/RECORD.md exactly as it says, step 3e included.

It writes Loadout's records only. Run only the commands it lists, each once with --dry-run first. Don't start a session, build, run, refine round or schedule, don't queue anything for a team, don't change any code, and don't open a branch or a pull request.

When you're done, tell me what each step did (updated, created, or skipped and why), and confirm that nothing was started.
```

The tasks it leaves (project `loadout-cli`):

| Task | Brief |
|---|---|
| `studio-workflow` | `WORKFLOW.md` |
| `studio-controls` | `CONTROLS.md` |
| `studio-office` | `OFFICE.md` |
| `team-run-visibility-and-reliability`, `visual-full-page-red-alert`, `add-task-drop-down-in`, `measure-loadout-s-value-to` | `ROUND4.md`, Parts 1 to 4 (one piece of work) |

### Starting one from Loadout

When you want one built, start a session on its task, from Studio's task board (Start) or the CLI:

```
loadout launch loadout-cli --task-id studio-workflow --task "Build this task: run 'loadout task show studio-workflow --project loadout-cli' and do what its detail says. I'm asking for it now."
```

The session is linked to the task, and the task's detail holds the same instructions as the build prompts below. For `ROUND4.md`, start it on any one of its four tasks; the brief builds all four parts and its Record it in Loadout updates every one.

## 2. Or paste a build prompt

### Build WORKFLOW.md: live work first, in every mode

```
Check out claude/determined-meitner-6wqi6z and pull it. Then build design/studio/WORKFLOW.md: all of it, as one piece of work.

- Read it to the end before you start. Then read the files its Source of truth lists, starting with design/studio/HANDOFF.md and design/studio/reference/README.md.
- Where the brief and the reference disagree, the brief wins. Where a design choice can't be built as written, stop and ask me rather than inventing.
- Main has moved on since the brief was written. Branch off claude/determined-meitner-6wqi6z as the brief says, then merge origin/main into your branch before you start, and keep anything main changed.
- Follow its Before you finish section, and commit in the logical steps it lists. Add no AI or co-author attribution lines to commits.
- Do Record it in Loadout as the very last step.
- Don't push or open a pull request until I say so.

When you're done, tell me what you built, what you left out and why, and the test results.
```

### Build CONTROLS.md: every command as a control on its page

```
Check out claude/determined-meitner-6wqi6z and pull it. Then build design/studio/CONTROLS.md: all of it, as one piece of work.

- Read it to the end before you start. Then read the files its Source of truth lists, starting with design/studio/HANDOFF.md and design/studio/reference/README.md.
- Where the brief and the reference disagree, the brief wins. Where a design choice can't be built as written, stop and ask me rather than inventing.
- Main has moved on since the brief was written. Branch off claude/determined-meitner-6wqi6z as the brief says, then merge origin/main into your branch before you start, and keep anything main changed.
- Follow its Before you finish section, and commit in the logical steps it lists. Add no AI or co-author attribution lines to commits.
- Do Record it in Loadout as the very last step.
- Don't push or open a pull request until I say so.

When you're done, tell me what you built, what you left out and why, and the test results.
```

### Build OFFICE.md: the office view of a team

```
Check out claude/determined-meitner-6wqi6z and pull it. Then build design/studio/OFFICE.md: all of it, as one piece of work.

- Read it to the end before you start. Then read the files its Source of truth lists, starting with design/studio/HANDOFF.md and design/studio/reference/README.md.
- Where the brief and the reference disagree, the brief wins. Where a design choice can't be built as written, stop and ask me rather than inventing.
- Main has moved on since the brief was written. Branch off claude/determined-meitner-6wqi6z as the brief says, then merge origin/main into your branch before you start, and keep anything main changed.
- Follow its Before you finish section, and commit in the logical steps it lists. Add no AI or co-author attribution lines to commits.
- Do Record it in Loadout as the very last step.
- Don't push or open a pull request until I say so.

When you're done, tell me what you built, what you left out and why, and the test results.
```

### Build ROUND4.md: round 4, including Red Alert

```
Check out claude/determined-meitner-6wqi6z and pull it. Then build design/studio/ROUND4.md: all of it, as one piece of work.

- Read it to the end before you start. Then read the files its Source of truth lists, starting with design/studio/HANDOFF.md and design/studio/reference/README.md.
- Where the brief and the reference disagree, the brief wins. Where a design choice can't be built as written, stop and ask me rather than inventing.
- Main has moved on since the brief was written. Branch off claude/determined-meitner-6wqi6z as the brief says, then merge origin/main into your branch before you start, and keep anything main changed.
- Follow its Before you finish section, and commit in the logical steps it lists. Add no AI or co-author attribution lines to commits.
- Do Record it in Loadout as the very last step.
- Don't push or open a pull request until I say so.

When you're done, tell me what you built, what you left out and why, and the test results.
```
