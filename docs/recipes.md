# Recipes

Worked answers to the things people actually arrive wanting. Every command here
is real; the deeper explanation lives in the page each section points at.

## Start work

```bash
loadout                         # pick from the list
loadout starstats               # go straight there
loadout here                    # launch whatever repository you are standing in
loadout starstats --agent codex # a different agent, just this once
```

A bare word that is not a command is treated as a project, which is what makes
`loadout starstats` work. Add `--task "what you are about to do"` and the
instruction set is chosen for that task rather than in general.

```bash
loadout starstats --task "the upload retries twice and then gives up" \
  --mode investigate
```

`--mode` is `implement` unless you say otherwise, and it is never inferred from
the words you typed. See [Specialists](specialists.md).

## Find out what a session will be told, before spending one

```bash
loadout instructions explain --project starstats "add a retry to the upload step"
```

It lists every specialist that would be loaded, the reason each one was chosen,
and what the whole set costs in tokens. This is the cheapest thing in the tool
and the one worth running first when a session behaves oddly — an agent that
ignored a rule usually never received it.

```bash
loadout instructions list                    # everything available
loadout instructions list --kind language
loadout instructions show language.csharp    # read one in full
```

## Register a project

```bash
loadout project add                     # the repository you are in
loadout project add --repo /src/atlas   # or one somewhere else
loadout project clone starstats         # a registered project onto this machine
loadout project new                     # a new repository from a template
```

`loadout project survey` reports agent state on this machine that no project
accounts for — memory an agent recorded before Loadout existed, or beside a
repository rather than in it. See [Memory](memory.md).

## Bring in memory an agent already recorded

```bash
loadout memory import starstats                 # finds the agent's own layout
loadout memory import --all                     # every project with some waiting
loadout memory import storefront --from <dir>   # or point at it directly
```

Nothing is written without `--apply`; the default is a preview. A topic holding
something credential-shaped is refused rather than committed, because the
workspace is a Git repository and importing a token would publish it.

## Fill a memory that is empty

There is nothing to import for a project nobody has recorded anything about.
Have the agent do the reading and write down what it finds:

```bash
loadout launch starstats --mode investigate \
  --task "review this codebase and record what you find"
loadout memory list starstats
```

`skill.repository-review` activates on that task and gives the agent the
procedure. The mode matters: a reviewing skill is withheld from `implement`.

## Record one durable fact yourself

```bash
loadout memory write starstats build-quirks \
  --description "things that surprise people about the build" \
  --fact "The first build after a clean takes four minutes; the analyzers warm up."
```

Facts that will be false next month belong nowhere. `loadout memory audit`
reports those, along with duplicates and index rot; `--clean --apply` removes
only what needs no judgement.

## Get the context bill down

```bash
loadout rules budget starstats     # what loads on every launch
loadout memory compress starstats  # move standing facts out of instructions
loadout rules split starstats      # scope prose to the paths it applies to
```

Instructions are inlined in full on every launch; memory is inlined only as an
index. Moving a standing fact across is the difference between paying for a
line every session and paying for one index entry. See
[The context budget](context-budget.md).

## Check the machine, and fix what is safe to fix

```bash
loadout doctor                     # what is wrong
loadout doctor --fix --dry-run     # what fixing it would change
loadout doctor --fix               # do it
```

Every file-changing command takes `--dry-run`, and a test enumerates the
command list to prove it. Anything that did change is recoverable:

```bash
loadout backup list
loadout backup restore 20260901-204044-fd3512
```

## Keep repositories clean

```bash
loadout protect starstats     # pre-commit hook that keeps agent files out
loadout drift                 # projects whose agent files have wandered
loadout migrate starstats     # move .claude/rules into the workspace
```

The point of all three is that agent configuration, prompts and memory live in
the workspace repository rather than in the application repository. See
[Repository cleanliness](repository-cleanliness.md).

## Move to another machine

```bash
loadout setup                 # point at the workspace repository
loadout workspace status
loadout workspace save        # commit and push what changed here
loadout workspace sync        # take what changed elsewhere
```

The workspace carries projects, instructions, rules and memory. It does not
carry anything machine-specific: a path on this disk, or the launcher's own
executable, stay local by design.

## Hand over to a different agent

```bash
loadout handoff starstats     # a document the next agent can read
loadout sessions              # what is resumable
```

## See what it has cost

```bash
loadout usage                      # last 30 days, by project
loadout usage --days 7 --by day
loadout statusline install         # branch, model and context in the agent's status line
```

Read out of the transcripts your agents already write, so there is history from
the moment you install this. See [Usage](usage.md).

## Let the agent ask the launcher things

```bash
loadout mcp list                   # servers this project would get
```

A launched agent is offered the launcher's own tools — reading a specialist,
asking what its effective instructions are, and recording one screened fact.
Nothing that changes the machine or pushes to a remote is offered, deliberately.
See [Commands](commands.md).

## See also

- [First run and configuration](first-run.md) — setup and `config.yaml`
- [Commands](commands.md) — the whole surface
- [The launcher](launcher.md) — the terminal UI, with pictures
