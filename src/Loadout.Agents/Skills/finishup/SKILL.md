---
name: finishup
description: End-of-session review. Reconstructs what actually happened from evidence, draws the lessons out of it, and records the ones worth keeping as durable memory in both stores. Use when the user runs /finishup, says they are wrapping up, or asks for a lessons-learned or retro on the session.
---

# Finish up

A session ends with things nobody wrote down. This turns them into a short
honest account and a small number of durable facts, then leaves both memory
stores in step.

Four phases. Do them in order — the sift depends on the evidence, and the
writes depend on the sift.

## 1. Reconstruct the session from evidence

Do not review from recall. What you remember of the session is exactly the part
most likely to be wrong, because the beliefs that got corrected mid-session are
the ones that felt true at the time.

Gather, before writing a word of the review:

```bash
git log --oneline -20                   # what landed
git status --short                      # what did not
git diff --stat                         # uncommitted shape
git stash list                          # anything parked
ls -la "$SCRATCHPAD"                    # probes, scripts, captured output
```

Then, for anything the session claimed:

- **A passing suite is a claim.** If the last real test output is not in the
  transcript, either run the covering tests again now or write "not re-verified"
  in the report. Never carry an earlier turn's green forward as fact.
- **A fix is a claim.** Name the evidence that it works. If the evidence was a
  theory that looked right, say so.
- **A CI or release state is a claim.** Check it or mark it unchecked.

Where a project has a launcher-managed context (`loadout` on PATH), also read
what the session was already told, so you do not "learn" something that was in
front of you the whole time:

```bash
loadout memory list
```

## 2. Draw the lessons

Work through these questions against the evidence. Most sessions answer two or
three of them; a session that answers none has no lessons, and saying so is a
valid result.

- **What took more than one attempt?** For each, why did the first attempt
  fail? The reason is the lesson, not the eventual fix.
- **What did we believe that turned out to be false?** Including things that
  were plausible. A disproved explanation is worth as much as a proved one,
  because the next session will find it plausible too.
- **What lied?** A log, a status field, a tool's output, a test that passed for
  the wrong reason. Instruments that mislead are the highest-value memories
  there are.
- **What did the environment do that a fresh session would not expect?** Shell
  quoting, path handling, a policy on this machine, a tool that behaves
  differently here.
- **What is true of the codebase that reading it does not reveal?** Intent
  behind a non-obvious choice, a constraint that is deliberate rather than
  accidental.
- **What did the user correct you on?** Their corrections are guidance, and
  guidance repeats.
- **What would the next session rediscover from scratch?** This is the one
  that catches everything the others miss.

## 3. Sift the candidates

A candidate becomes a memory only if all three hold:

1. **It will still be true next month.** Not the state of a branch, not a bug
   currently open, not "we are halfway through X". Those go in a handoff.
2. **A next session cannot get it free.** If the repository, the git history,
   CLAUDE.md or the compiled instructions already say it, recording it costs
   context every launch and returns nothing. Check with
   `loadout memory find "<the claim>"` before assuming it is new.
3. **It changes what someone does.** A fact nobody acts on is trivia.

Two hard rules on content:

- **Only what the session observed.** Anything inferred goes in the report as
  "worth confirming", never into memory. A memory carries no hedge with it into
  the next session — it arrives as settled fact.
- **Never a secret value.** If the lesson is about a credential, record the
  pattern, the location and the type. Never the value, in any store.

Route each survivor to a scope:

| Scope | For | Where it lands |
|---|---|---|
| `project` | true of this codebase | `projects/<slug>/memory/` |
| `user` | true of this person's work anywhere | `workspace/memory/` |
| `machine` | true only of this machine | machine-local, never synced |

The scope test is "would this still be true on a different machine, or on a
different project?" A policy on this Windows box is `machine`. A preference
about how commits are written is `user`.

## 4. Write, to both stores, in step

There are two memory stores and nothing keeps them synchronised. Both are read
into context at session start, so a fact recorded in only one is a fact that
half your future sessions do not get — and topics have already drifted apart
this way.

**Store A — the launcher workspace.** Git-backed, shared across machines and
across agents. Write here first, because it screens what you write:

```bash
loadout memory write <topic-slug> \
  --description "<what question this topic answers>" \
  --fact "<one standing claim>" \
  --kind lesson \
  --scope project
```

Notes bought the hard way:

- `--kind` is `project`, `decision`, `lesson` or `reference`. A lesson learned
  from something going wrong is `lesson`.
- The description must say **what question the topic answers**, not restate its
  own name. `loadout` rejects a description that echoes the slug, because only
  that line reaches a future session's context.
- Each `--fact` must make a **standing claim** — what is true, what is required,
  or why something is the way it is. "Investigated the build" is refused.
- `write` merges into an existing topic that covers the same ground, and will
  refuse and name the neighbours it thinks you mean. Pass `--separate` only
  when it genuinely is a new topic.
- Add `--dry-run` first if you are unsure the wording will pass.

**Store B — the agent's own project memory.** Machine-local, per project, and
the copy this session actually had in context. Its directory is derived from the
repository path with `:`, `/`, `\` and `.` all replaced by `-`:

```
D:\git\ailauncher  ->  ~/.claude/projects/D--git-ailauncher/memory/
```

Mirror it as a **verbatim copy** of the file the launcher just wrote — same
frontmatter, same body, no summarising and no reformatting.

Then update each index, and note they are **not the same format**:

- The workspace `MEMORY.md` is generated. Do not hand-edit it — run
  `loadout memory reindex`.
- The agent-side `MEMORY.md` is hand-written, one line per topic, ordered by
  when it was added:

  ```
  - [Title Case Hook](topic-slug.md) — the hook, in a clause, em dash not hyphen
  ```

Finally, check the result rather than assuming it:

```bash
loadout memory audit
```

## 5. Unfinished work is a handoff, not a memory

Anything that fails test 1 in the sift — in-flight state, a half-finished
change, what you were about to do next — belongs in a handoff, where it expires
naturally:

```bash
loadout handoff <project>
```

Worth doing whenever the work is unfinished. You know now what the next session
would have to rediscover; after this turn, nobody does.

## Report

Close with a short block to the user, not a wall of text:

- **What happened** — what landed, in one or two lines.
- **Not verified** — everything you could not confirm, named plainly. This
  section existing is the point of the exercise.
- **Recorded** — each memory written, with its scope, as one line.
- **Deliberately not recorded** — candidates you rejected and why.

Do not commit anything, do not push, and do not sync the workspace unless the
user asks. Recording a fact is not the same as publishing one.
