# Recipes

The jobs people actually turn up wanting to do. Every command here is real; the
page each section points at has the detail.

## Start work

```bash
loadout                         # pick from the list
loadout starstats               # go straight there
loadout here                    # launch whatever repo you're standing in
loadout starstats --agent codex # use a different agent, just this once
```

A first word that isn't a command is treated as a project name, which is what
makes `loadout starstats` work.

Tell it what you're doing and it picks the instructions for that, rather than in
general:

```bash
loadout starstats --task "the upload retries twice then gives up" \
  --mode investigate
```

`--mode` defaults to `implement`, and it's never guessed from what you typed. It
holds for the whole session, not one message.

If the work changes shape part way through — you asked for a look at a bug and
now you want the fix — the agent can switch it itself with the `loadout_mode`
tool, or you can see what a different posture would give you:

```bash
loadout instructions explain --mode implement --project starstats "add the retry"
```

The language and framework specialists don't change with the mode; they come
from what's in the repo. What changes is the posture and which skills are on
offer. See [Specialists](specialists.md).

## Check what a session gets, before you spend one

```bash
loadout instructions explain --project starstats "add a retry to the upload step"
```

You get every specialist it would load, why each one was picked, and what the
whole lot costs in tokens. It's the cheapest command here and the first one to
run when an agent behaves oddly — usually it never got the rule you thought it
had.

```bash
loadout instructions list                    # everything available
loadout instructions list --kind language
loadout instructions show language.csharp    # read one in full
```

## Register a project

```bash
loadout project add                     # the repo you're in
loadout project add --repo /src/atlas   # or one somewhere else
loadout project clone starstats         # a registered project onto this machine
loadout project new                     # a new repo from a template
```

`loadout project survey` finds agent state on this machine that no project
accounts for — memory an agent wrote before you installed Loadout, or memory
sitting next to a repo instead of in it. See [Memory](memory.md).

## Bring in memory an agent already wrote

```bash
loadout memory import starstats                 # finds the agent's own layout
loadout memory import --all                     # every project with some waiting
loadout memory import storefront --from <dir>   # or point at it directly
```

Nothing is written without `--apply`. You get a preview first. A topic with
something credential-shaped in it is refused rather than committed, because the
workspace is a Git repo and importing a token would publish it.

## Fill a memory that's empty

If nobody's recorded anything about a project, there's nothing to import. Get
the agent to do the reading and write down what it finds:

```bash
loadout launch starstats --mode investigate \
  --task "review this codebase and record what you find"
loadout memory list starstats
```

`skill.repository-review` activates on that task and hands the agent the
procedure. You need the mode: review skills don't load in `implement`.

## Write down one fact yourself

```bash
loadout memory write build-quirks --project starstats \
  --description "things that surprise people about the build" \
  --fact "The first build after a clean takes four minutes; the analyzers warm up."
```

Don't record anything that'll be false next month. `loadout memory audit` finds
those, plus duplicates and index rot. `--clean --apply` removes only the things
that need no judgement.

## Get the context bill down

```bash
loadout rules budget starstats     # what loads on every launch
loadout memory compress starstats  # move standing facts out of instructions
loadout rules split starstats      # scope prose to the paths it applies to
```

Instructions load in full every time. Memory only loads as an index. Moving a
standing fact across is the difference between paying for a whole line every
session and paying for one index entry. See
[the context budget](context-budget.md).

## Check the machine, fix what's safe to fix

```bash
loadout doctor                     # what's wrong
loadout doctor --fix --dry-run     # what fixing it would change
loadout doctor --fix               # do it
```

Every command that changes a file takes `--dry-run`, and a test walks the whole
command list to prove it. Anything that did change, you can put back:

```bash
loadout backup list
loadout backup restore 20260901-204044-fd3512
```

## Keep repos clean

```bash
loadout protect starstats     # pre-commit hook that keeps agent files out
loadout drift                 # projects whose agent files have wandered
loadout migrate starstats     # move .claude/rules into the workspace
```

All three exist so agent config, prompts and memory live in the workspace
instead of your application repo. See
[Repository cleanliness](repository-cleanliness.md).

## Move to another machine

```bash
loadout setup                 # point at the workspace repo
loadout workspace status
loadout workspace save        # commit and push what changed here
loadout workspace sync        # take what changed elsewhere
```

The workspace carries projects, instructions, rules and memory. It doesn't carry
anything machine-specific — a path on this disk, or where the binary lives, stay
local on purpose.

## Hand over to a different agent

```bash
loadout handoff starstats     # a document the next agent can read
loadout sessions              # what you can resume
```

## See what it's cost

```bash
loadout usage                      # last 30 days, by project
loadout usage --days 7 --by day
loadout statusline install         # project, branch and context in the status line
```

Read out of the transcripts your agents already write, so you have history from
the day you install it. See [Usage](usage.md).

## Let the agent ask Loadout things

```bash
loadout mcp list                   # servers this project would get
```

A launched agent gets the launcher's own tools: read a specialist, ask what its
instructions are, record one fact, and change its mode when the work changes
shape. It doesn't get anything that changes your machine or pushes to a remote.
See [Commands](commands.md).

## See also

- [What you get](features.md) — every part of it, with what the commands print
- [First run](first-run.md) — setup and `config.yaml`
- [The launcher](launcher.md) — the terminal UI
