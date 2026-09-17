# Getting started

[Recipes](recipes.md) is the lookup table — you know the job, you want the
command. This is the other thing: the first hour with Loadout, in order, and
what's worth doing a week later once the first hour has stopped being
interesting.

## What it assumes you already have

An agent. Loadout launches Claude Code or Codex; it doesn't replace either, and
it won't install one for you. If neither is on your PATH, `loadout doctor` says
so on the first run rather than failing later with something less obvious.

Git, and a repository you actually work in. A directory that isn't a repository
yet is fine — it registers as one still to be set up — but the point of the
thing is your own code.

Nothing else. No service, no account, no container.

## The first ten minutes

```sh
loadout setup                  # where the workspace lives, which agent you use
loadout project add .          # register the repo you're standing in
loadout protect                # keep agent files out of it
loadout                        # the launcher
```

`setup` offers three ways to keep a workspace and treats them as equals: point
at one that already exists, create one, or run with no central storage at all.
The last isn't a degraded mode — it lays out the same directories locally, so
adopting a shared workspace later is a matter of pushing what you already have.
Take the Git-backed one if you work on more than one machine, because the
workspace is what carries your instructions, rules and memory between them.

It then checks Git is there, sets a global identity if you have none (without
it every workspace commit fails with "Author identity unknown"), picks a secret
provider that works on this machine, lists the repositories it found in your
development roots and offers to register them, and offers to move any agent
files it finds in them into the workspace.

So `project add` may be redundant by the time you get to it. It's there for the
repository setup didn't find, and for the one you clone next week. It registers
under a slug, and that slug is what `loadout <project>` takes and what memory,
rules and launch history get filed under.

`protect` installs a pre-commit hook that stops agent tooling files being
committed. It's POSIX shell and it works the check out from Git rather than
calling back into `loadout`, so it keeps working on a machine where you've moved
the binary, and a hook the launcher didn't write is never touched. Hooks live in
`.git/hooks` and never travel, so a fresh clone is unprotected until somebody
notices — which is why a launch says so on the way in, and why it warns rather
than blocks. An unprotected repository is still one you might have good reason
to work in.

Then `loadout` on its own, and you're at the list.

## Your first launch

Enter on a project doesn't start anything. It opens the launch sheet: four
questions, already answered with the defaults, and Enter again starts the
session. One keystroke buys you the preview.

The four are the agent, the task, the mode, and the profile or worktree. Only
two of them decide much:

**The task** is a sentence saying what you're about to do. "the upload retries
twice then gives up" is a task; "work on the API" isn't, and won't reach
anything the file extensions wouldn't have reached anyway. It's the field that
does the most and the one nobody fills in — here it was typed twice in 122
launches — so if the project has declared what it's working on, the sheet fills
it in for you and you correct the line rather than composing one.

**The mode** is the posture for the whole session: `implement`, `investigate`,
`review` or `advise`. It is never guessed from what you typed, and it defaults
to `implement`, which assumes you've already decided what to do. That default is
the commonest reason a session doesn't behave the way somebody expected — ask
for a bug to be investigated in `implement` and the investigation skills don't
load at all.

Underneath, the sheet shows what that combination actually resolves to:
specialists, the reason each was picked, and what the lot costs in tokens. It
isn't a preview drawn separately from the launch; it's the launch's own resolver,
asked early.

From the command line the same thing is:

```sh
loadout starstats --task "the upload retries twice then gives up" --mode investigate
loadout starstats --dry-run
```

`--dry-run` describes the whole launch and starts nothing: the executable, the
working directory, the compiled context and where it went, the environment
variables by name, the full command line, every specialist and why. Worth
running once just to see what a launch is.

## When the agent does something daft

Run this before anything else:

```sh
loadout instructions explain --project starstats "what you asked it to do"
```

Most of the time the answer is there and it's dull: it never got the instruction
you thought it had. Either the task line didn't say enough to reach the
specialist, or the mode was wrong, or the rule you're thinking of is scoped to
paths this work doesn't touch. Guessing at the prompt is the expensive way to
find that out.

If it genuinely got the guidance and ignored it, that's worth knowing too, and
it's a different problem from the one you assumed you had.

## Ending one

When the agent exits, anything it changed in the workspace gets screened and
you're asked what to do with it — save and sync, save locally, look at it first,
or leave it. Leave it is a real option and nothing is deleted.

A session that ran for a while and left no handoff gets told so on the way out,
with the command. Said rather than done: a handoff written automatically would
be a document with nothing in it, and the next session would be handed something
that says nothing and believe it had been handed over to.

```sh
loadout handoff starstats     # write one
loadout sessions              # what you could resume
loadout resume --last
```

## The week after

The first hour is registration. What makes the difference afterwards is the
stuff you'd never bother with on day one.

**Write things down.** The first time you work out something non-obvious — why
the build takes four minutes after a clean, why that test only fails in CI —
put it somewhere a session will find it:

```sh
loadout memory write build-quirks --project starstats \
  --description "things that surprise people about the build" \
  --fact "The first build after a clean takes four minutes; the analyzers warm up."
```

Not a diary. A fact that'll be true next month, with a description somebody can
choose from — "notes" costs every session's attention and tells it nothing.
See [Memory](memory.md).

**Look at what every session is paying for.**

```sh
loadout rules budget starstats     # what loads whatever the task
loadout memory compress starstats  # move standing facts out of instructions
```

Instructions load in full every launch. Memory loads as one line per topic. A
standing fact sitting in an instruction file costs you the whole line every
session; the same fact in memory costs one index entry. That's the whole of
[the context budget](context-budget.md), and `memory compress` moves them across
verbatim — nothing is reworded, and nothing leaves the source until it has been
read back out of the store.

**See what it's cost.**

```sh
loadout usage --days 7 --by day
```

No setup and no opt-in — it reads the transcripts your agents already write, so
there's history from before you installed it.

**Register the second project, then the rest.** `loadout project discover`
scans the folders you configured. A clone on a new machine is unprotected until
somebody notices, so the launcher says so on the way in rather than leaving it
to be found later.

## What's not worth bothering with yet

Profiles, worktrees, packs, checkpoints, spend thresholds and the telemetry
receiver are all real and all documented, and none of them earn their keep on
day one. They're in [First run](first-run.md) and [Commands](commands.md) when
a specific irritation sends you looking.

The same goes for writing your own specialists. Read a few shipped ones first —
`loadout instructions show language.csharp` — because most of what people want
to write is already in the library, and the ones that aren't are usually about
this codebase specifically, which is what project rules and memory are for.

## See also

- [Working with a coding agent](agentic-coding.md) — the habits, whatever
  launcher you use
- [Recipes](recipes.md) — the same jobs as a lookup table
- [What you get](features.md) — every part of it, with what the commands print
- [First run](first-run.md) — `config.yaml`, environments, security profiles
