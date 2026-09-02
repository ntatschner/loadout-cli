# Specialists and skills

An agent launched at a repository needs to know two different things: what it
should know, and what it should do. Specialists answer the first, skills the
second, and the point of both is that a session gets the small relevant set
rather than everything the project has ever touched.

This is the fourth layer of the model in [the context budget](context-budget.md),
and it is priced the same way: what loads on every launch costs on every launch.

## Specialist or skill

A **specialist** is expertise. It says what to care about, what to avoid, what
to inspect and when to hand over to somebody else. `database.postgresql` knows
that the estimate-versus-actual gap in a plan usually means stale statistics.

A **skill** is a procedure. It is a numbered list you can follow and know when
you have finished. `skill.query-optimisation` is ten steps ending in "confirm
the result set is unchanged".

The difference matters when writing one. A skill that reads like a description
of a subject is a specialist in the wrong folder.

## Layers

Guidance composes from the general to the specific, so where two sources
disagree the narrower one is read last.

| Layer | What it is | Example |
|---|---|---|
| Foundation | Always on, deliberately small | `foundation.change-safety` |
| Mode | Posture for this piece of work | `mode.investigate` |
| Language | What the code is written in | `language.csharp` |
| Framework | What it is built on | `framework.ef-core` |
| Database | Which engine | `database.postgresql` |
| Platform | Where it runs | `platform.docker` |
| Cloud | Whose cloud | `cloud.azure` |
| Function | A cross-cutting specialty | `function.performance` |
| Skill | A procedure to follow | `skill.query-optimisation` |
| Project | The project's own instructions | `projects/<slug>/context/` |

Composition is by dependency, not inheritance. `framework.aspnet-core` requires
`framework.dotnet`, which requires `language.csharp`; nothing subclasses
anything, and the chain is three deep at most.

## Modes

Four postures, which change what to do rather than what to know.

| Mode | Posture |
|---|---|
| `advise` | Answer the question. Do not change the repository. |
| `investigate` | Find out what is happening. The fix is a separate step. |
| `implement` | Make the change and verify it. The default. |
| `review` | Judge the change as written. Report, do not rewrite. |

Modes combine with specialists rather than duplicating them. `performance` with
`investigate` means measure and isolate before editing; `performance` with
`implement` means apply an optimisation that has already been justified.

### A mode lasts the whole session

A mode is a directive for the session, not for a message. You set it at launch
with `--mode`, and it holds until something changes it. It's never guessed from
what you typed, so a session started in `implement` stays there even if your
next message is a question. That's deliberate: a posture that flipped on the
wording of one message wouldn't be a posture.

### Changing it part way through

Work changes shape. You ask an agent to look into a bug, it finds the cause, and
now you want the fix — that's `investigate` becoming `implement`. The agent can
make that switch itself:

```text
loadout_mode(mode: "implement", task: "add the retry to the upload step")
```

or, without the launcher's own tools:

```bash
loadout instructions explain --mode implement --project starstats "add the retry"
```

Either gives back the posture to adopt and what now applies. `skill.mode-switch`
tells the agent when this is worth doing and when it isn't — one question inside
a piece of implementation work is not a mode change.

An unrecognised mode is refused rather than quietly treated as the default,
because an agent told nothing would carry on believing it had switched.

### What a mode change doesn't touch

Only two things move: the posture, and which skills are on offer. A reviewing
skill is available in `investigate`, `advise` and `review` and withheld from
`implement`.

Everything else keeps working. Language, framework, database and platform
specialists come from what's actually in the repository, so they apply whatever
the mode is. Specialists triggered by task phrases keep triggering on the words
in the new task. Nothing already in the context is taken away.

## How a specialist gets loaded

Four ways, in descending order of authority.

**You asked for it.** `--specialist function.security` loads it whatever the
evidence says, and it is never dropped for budget. Asking for something and
silently not getting it is the worst outcome available, so naming a specialist
that does not exist stops the command rather than proceeding without it.

**The task says so.** The strongest automatic signal, because it is the only one
that reflects what you are actually doing. Phrases are matched on whole words:
`api` does not match "capital".

**A dependency declares it.** `Npgsql` in a project file is a deliberate choice
and worth more than a file extension.

**The repository looks like it.** The weakest signal, and restricted on purpose.
Files and dependencies activate **languages and frameworks only** — the layers
that describe what the code *is*. Databases, platforms, clouds and functional
specialists describe what you are *doing*, and a repository contains all of
those whatever today's task is. This is the rule that stops somebody fixing a
null reference being handed PostgreSQL, Kubernetes and Azure.

A language also has to clear a threshold of three files. One stray `.sql` file
does not make a project a database project.

## Project preferences

A project or profile can say which specialists it expects to be relevant:

```yaml
# projects/<slug>/project.yaml
specialists:
  preferred:
    - language.csharp
    - framework.dotnet
    - database.postgresql
  excluded:
    - cloud.aws
  mode: implement
```

**Preferred does not mean always loaded.** For a language or framework, a
preference plus supporting evidence is enough. For anything else, a preference
raises the specialist's standing — so it is not the first thing dropped when the
budget is tight — but the task still has to point at it. A project that lists
four technologies uses all four; that is not a reason to put all four in front
of every session.

**Excluded is honoured.** It beats every kind of inference, including a
requirement from another specialist. The one thing that overrides it is naming
the specialist explicitly on the command line.

A profile's preferences replace the project's rather than adding to them, so a
profile can narrow.

## Seeing what an agent will be told

```bash
loadout instructions explain "Why is this EF Core PostgreSQL query taking 12 seconds?" \
  --mode investigate
```

```text
Effective agent instructions

foundation
  + Change safety           always applies
  + Engineering core        always applies
  + Evidence first          always applies
  + Verification            always applies

mode
  + Investigate             investigate mode

language
  + C#                      required by framework.dotnet

framework
  + .NET                    required by framework.ef-core
  + Entity Framework Core   task mentions "ef core"

database
  + PostgreSQL              task mentions "postgresql"

function
  + Database                task mentions "query"
  + Performance             task mentions "taking"

skill
  + Database query optimisation   task mentions "query taking"

Context
  Estimated instruction tokens: 2,853
  Budget: 12,000
  Usage: 24%
```

Every line carries the reason it is there, and `--json` gives the same
information as a document. The explanation runs through the same service the
launch path uses, so it describes what would actually be composed.

## Reading one

`explain` says which specialists a task would load. To read what one of them
actually says:

```bash
loadout instructions show language.rust
```

```text
Rust  language.rust
built-in, about 214 tokens

Loads when
  task mentions: rust, cargo, borrow checker
  files match: **/*.rs, **/Cargo.toml

## Cares about

Lifetimes and ownership, and what the code does when something fails.
...
```

The whole text, what triggers it, and what it costs. Worth reading before
writing your own version of one: the shipped specialists are deliberately
short, and a project specialist with the same id replaces rather than
supplements the one it shadows.

To see what there is at all:

```bash
loadout instructions list            # everything available to this project
loadout instructions list --kind language
```

The library ships 73 specialists — 4 foundations, 4 modes, 10 languages, 8
frameworks, 4 databases, 5 platforms, 3 clouds, 22 functions and 13 skills.
They are embedded in the binary rather than kept on disk, so the command is the
way to read them; there is no directory to browse.

## The context budget

More guidance is not better. The ceiling is in estimated tokens:

```bash
loadout config set instruction-max-tokens 12000
loadout config set instruction-warn-percent 80
```

Bytes are the exact measure and tokens are an estimate — no tokeniser here
matches the providers' — so both are reported and the token figure is always
labelled as approximate.

When the candidates exceed the budget, whole specialists are dropped, weakest
evidence first. Nothing is ever cut in half: half a specialist reads as complete
guidance while missing the caveat that made it safe. Foundation, the mode and
anything you named explicitly are never dropped, and everything omitted is
reported with both the reason it was reached and the reason it went.

## Turning it off

```bash
loadout config set specialists false
```

Launches then compile exactly the context they did before this existed. Offered
because this changes what every existing session is told, and somebody who has
tuned their own instructions for a year is entitled to decline.

## Where specialists live

Three layers, later overriding earlier by id:

1. **Built in.** Shipped inside the launcher. Not on disk, so they cannot be
   edited in place, go missing from an install, or be made to point elsewhere.
2. **Workspace** — `global/specialists/` in the central workspace, shared across
   every machine that clones it.
3. **Project** — `projects/<slug>/specialists/`, for one project only.

To disagree with a built-in specialist, write one with the same id in your
workspace. There is no need to change the launcher, and no registry to keep in
step.

## Writing one

Start with a draft rather than a blank file:

```bash
loadout instructions new skill.deploy-checklist
loadout instructions new language.rust --project starstats
```

It writes into the workspace library, or into one project's with `--project`,
under the directory its layer keeps. The draft is valid when it lands, and only
carries the activation its layer can use: a language is found by what is in the
repository, a skill by the words of a task, and a foundation applies always and
has nothing to decide. `--dry-run` prints it instead of writing it.

The library is read back immediately afterwards, so a draft that does not load
says so at the moment you made it rather than the next time something needed it.

One markdown file that describes itself. There is no manifest: the library is
what is on disk, which removes the possibility of a registry naming a file that
is not there.

```markdown
---
id: database.postgresql
kind: database
title: PostgreSQL
summary: Planner behaviour, indexing, MVCC and locking.
dependencies:
  - 'Npgsql'
  - 'psycopg'
task_phrases:
  - 'postgres'
  - 'postgresql'
globs:
  - '**/*.sql'
requires:
  - 'function.database'
---

## Cares about

What the planner chose and why, and what a statement locks.

## Working rules

- Read `EXPLAIN (ANALYZE, BUFFERS)`, not `EXPLAIN`.
...
```

| Field | Meaning |
|---|---|
| `id` | Dotted identifier. Its first segment must match `kind`. |
| `kind` | One of the layers above. An unknown kind fails to load. |
| `always` | Foundation only. Loads whatever the task. |
| `task_phrases` | Words in the task that suggest it. The strongest signal. |
| `dependencies` | Substrings of a declared dependency. |
| `globs` | Repository paths that suggest it. |
| `requires` | Other specialists needed for this one to make sense. |
| `modes` | Restrict to particular postures. Empty means all. |
| `capabilities` | Agent capabilities needed before it is worth loading. |

Content should answer seven questions: what it cares about, what mistakes to
avoid, what to inspect, what evidence to gather, which trade-offs matter, how to
verify, and when to defer to another specialist. Keep it short enough that
composing four of them is still practical.

Do not write a persona. "You are the world's greatest PostgreSQL guru" tells an
agent nothing; "a large gap between estimated and actual rows usually means
stale statistics" tells it something.

Check your work:

```bash
loadout instructions validate --strict
```

This is also folded into `loadout doctor`, so a broken specialist shows up
wherever every other problem does.

## Trust

Specialist files are instructions to an agent that can edit code and run
commands, so whoever controls the file controls the agent. Accordingly:

- Files are read only from inside the library directories. A link resolving
  outside one is refused and reported.
- Ids are addresses and are never resolved against the filesystem.
- Content is used verbatim. There is no template substitution of any kind, so a
  shared workspace cannot make the launcher interpolate an environment variable
  into text bound for an agent.
- A skill that names a script does not cause it to run. Loading a specialist
  executes nothing, however the file is phrased.
- Provenance is visible: `loadout instructions list` marks anything that came
  from your workspace or project rather than the launcher.

## Providers

Loadout composes provider-neutral guidance and each adapter delivers it the way
its agent expects. Provider formats — `AGENTS.md`, `CLAUDE.md`, `.cursor/rules`
— are outputs, never the source of truth.

Where an agent lacks a capability a specialist needs, the specialist is left out
with a reason rather than loaded into something that cannot use it. Capabilities
are probed against the installed CLI, not inferred from a version number.
