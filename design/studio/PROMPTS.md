# Prompts for a local agent

Paste one prompt per session, in a clone of `ntatschner/loadout-cli` with the Loadout CLI on the PATH. Run the records prompt once, first. After that, the four build prompts can go in any order, one session each: none of the briefs depends on another, and each brief's **Order** paragraph says how to fit in with any that is already built.

Every brief branches off `claude/determined-meitner-6wqi6z`, where the designs live, and ends with **Record it in Loadout**. No brief pushes or opens a pull request until you say so.

## 1. Record the designs (once, first)

```
Check out claude/determined-meitner-6wqi6z and pull it. Then do design/studio/RECORD.md exactly as it says.

It writes Loadout's records only. Run only the commands it lists, each once with --dry-run first. Don't start a session, build, run, refine round or schedule, don't change any code, and don't open a branch or a pull request.

When you're done, tell me what each step did (updated, created, or skipped and why), and confirm that nothing was started.
```

## 2. Build WORKFLOW.md: live work first, in every mode

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

## 3. Build CONTROLS.md: every command as a control on its page

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

## 4. Build OFFICE.md: the office view of a team

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

## 5. Build ROUND4.md: round 4, including Red Alert

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
