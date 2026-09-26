# Adding a project

**At the end:** a repository registered with Loadout, protected, onboarded by
its first session, and its `project.yaml` saying what you want every session in
it to start with — which agent, which model, what context, which credentials.

[Your first run](first-run.md) registers one repository to get you going. This
is the same job done properly, for the second project and every one after it,
and it's where every setting a project can carry is listed.

## Before you start

- A workspace, from `loadout setup`. `loadout doctor` reports it.
- The repository on this machine. A directory that isn't a Git repository yet
  can be registered too; step 3 says what happens then.

## Steps

### 1. Say what a new project should start with

Skip this if every project you have uses the built-in defaults. Otherwise, set
them once and every registration fills them in:

```sh
loadout config set onboarding-agent codex
loadout config set onboarding-model big-model
loadout config set onboarding-models "review=small-model;implement=big-model"
loadout config set onboarding-editor Agents
loadout config set onboarding-run skip      # only if you never want step 5
```

They fill blanks only. A project whose manifest already names an agent chose
that, and a preference on this machine isn't grounds to overrule it. `claude` is
the one exception, because it's the built-in default rather than anybody's
choice. More in [Onboarding defaults](../first-run.md#onboarding-defaults).

### 2. Preview it

```sh
cd path/to/your/repository
loadout project add --dry-run
```

```text
Would register D:\src\storefront as 'storefront'. Nothing was changed.
```

The slug is the short name you'll type — `loadout storefront` — and what memory,
tasks and launch history are filed under. It's taken from the remote, or the
directory name when there isn't one. If it isn't what you want, choose it now:
`--slug shop`. Changing it later means re-registering.

### 3. Register it

```sh
loadout project add
```

```text
Registered storefront (storefront)
  agent: codex (from your defaults)
```

Every default it filled in is listed, so nothing arrives unannounced. Registering
writes to the workspace, never to the repository. It also queues the project's
onboarding, which step 5 covers.

If the workspace already has a manifest under that slug — written on another
machine, or by hand — registering adopts it rather than replacing it, so the
settings you gave the project elsewhere carry over. That project isn't queued
for onboarding again: somebody has been there before.

If the directory isn't a Git repository yet, it's registered as one still to be
set up: a task saying so is recorded, and the project's tasks are put in front of
its sessions, so the first session is told to set the repository up before
anything else.

The other ways in:

| You have | Run |
|---|---|
| The repository somewhere else | `loadout project add path/to/it` |
| Several repositories under your development folders | `Ctrl+N` in the launcher finds them and registers the ones you pick; `loadout project discover` only lists them |
| A project registered on another machine, not cloned here | `loadout project clone <slug>` |
| Nothing yet, but a project to copy the setup from | `loadout project new <name>` |

### 4. Protect it

```sh
loadout protect
```

This installs a pre-commit hook and Git excludes that stop agent files being
committed. If the repository already has agent files committed — a `CLAUDE.md`,
an `AGENTS.md`, a `.claude` directory — `loadout migrate` moves them into the
workspace, shows you the change first and takes a snapshot you can restore.

### 5. Let the first session onboard it

Start a session with no task of its own:

```sh
loadout storefront
```

Because the project is waiting to be onboarded, that session does it instead of
waiting for work. It starts in investigate mode with the project onboarding skill,
the launch says so before the agent starts, and "Onboard this project" is sent as
its first message, so it gets going without you typing anything. An agent you
described yourself in `config.yaml` has no known way to take a first message; the
launch tells you what to type instead. The agent:

- asks Loadout what it already knows: what a session is given, where the code
  departs from its specialists, what every launch costs, any memory an agent left
  on this machine before Loadout, and whether the repository is protected;
- learns the code, and runs the build and the tests;
- records what stays true — how to build and test it, the traps, the decisions —
  in the project's memory;
- proposes changes to `project.yaml`, each with its reason, rather than making
  them;
- marks the onboarding done.

Anything that would change the repository or the workspace — importing memory,
protecting, migrating — is reported for you to run, not run.

A session started with a task of its own, say `loadout storefront --task "fix the
upload retry"`, isn't taken over. It's reminded that onboarding is still to do,
and gets on with what you asked.

To onboard now, or again after the project has changed shape, or to skip it:

```sh
loadout project onboard storefront          # start the onboarding session now
loadout project onboard storefront --skip   # record that you're skipping it
```

Either way it's recorded on the project as the task `onboard-project`: done when
an agent did it, dropped when somebody skipped it, with who and when.
`loadout task list --project storefront --all` shows it, and so does every other
machine once the workspace is saved.

### 6. Review what it proposed

```sh
loadout project proposal storefront
```

This shows each proposed change against the current file, with the agent's
reasons. Apply it or throw it away:

```sh
loadout project proposal storefront --apply
loadout project proposal storefront --discard
```

A proposal can't touch the project's identity, its repository, or its
`environment` and `environments` sections. Those decide which credentials and
which sandbox a session gets, and a change there has to be yours, made in the
file. A proposal made before somebody edited `project.yaml` is refused rather than
applied over their edit: discard it and ask for a new one.

### 7. Find the manifest

```sh
loadout project show storefront
```

The project lives in your workspace, under `projects/storefront/`:

```text
projects/storefront/
  project.yaml                  the settings below
  agents/claude/instructions.md what only Claude is told about this project
  context/                      instruction files the manifest names
  rules/                        rules, loaded by path
  memory/                       durable facts from earlier sessions
  tasks.yaml                    open work, including the onboarding
  proposals/settings.yaml       a settings change waiting for you, if any
```

`project.yaml` and `tasks.yaml` exist from registration. The rest appears when
something is written there. The manifest holds no local paths, so the same file works on every machine
you use; where the repository sits on this one is kept in this machine's own
configuration.

### 8. Change what you need

Edit `project.yaml` in the workspace. Everything it can say is listed
[below](#every-setting-in-projectyaml). Most projects change very little: the
common edits are a model, a context file or two, and a credential.

Two switches have their own command, so you don't need to open the file for them:

```sh
loadout project context tasks on      # show open tasks to every session
loadout project context code-map on   # inline a map of the code
```

### 9. Check what a session would get

```sh
loadout instructions explain --project storefront
loadout storefront --dry-run
```

The first says what would load and why; the second shows the launch it would make
— the agent, the directory, the mode and how much context — without starting
anything. Do this before the first
real session: an instruction file named wrongly is reported here rather than
discovered halfway through a task.

### 10. Save the workspace

```sh
loadout workspace save
```

That commits the manifest, and pushes it if your workspace is Git-backed, so your
other machines get it on their next sync. It commits **everything** pending in the
workspace, not just this project, so if another session has been writing there,
look at `git status` in the workspace first.

## Every setting in project.yaml

A freshly registered project looks like this. Anything left at its default can
be deleted from the file; it reads back the same.

```yaml
schema_version: 1
id: 3f0c...                    # written once; don't edit
slug: storefront
name: Storefront
aliases: []
repository:
  remote: https://github.com/example/storefront.git
  default_branch: main
  versioned: true
agents:
  default: claude
  enabled: []
  model: ''
  model_by_mode: {}
  settings: {}
context:
  global: []
  project: []
  code_map: false
  tasks: false
profiles: {}
launch:
  working_directory: repository
environment: {}
environments: {}
workspace:
  sync_on_launch: true
  save_on_exit: prompt
specialists:
  preferred: []
  excluded: []
  mode: ''
symbols:
  ignore: []
  extensions: {}
  languages: []
```

### Identity

| Setting | What it does |
|---|---|
| `id` | The project's permanent identity. It survives a rename and a change of remote, which is why it's never edited. |
| `slug` | The short name on the command line. Set by `--slug` at registration. |
| `name` | What the launcher shows. |
| `aliases` | Other names that find the same project. These are copied into the workspace's `registry/projects.yaml` when the project is registered, and it's the registry that resolves a name, so an alias added to the manifest afterwards also needs adding to the project's row there. |

### repository

| Setting | What it does |
|---|---|
| `remote` | The canonical remote. How a clone on another machine is recognised as this project. |
| `default_branch` | The branch Loadout treats as the base. |
| `versioned` | `false` for a directory registered before it was a Git repository. Anything that reads Git checks this and says nothing, rather than reporting a missing repository that is missing on purpose. |

### agents

| Setting | What it does |
|---|---|
| `default` | The agent launched when none is named. It wins over the registry and over `default-agent` in your configuration. |
| `model` | The model to start the agent on, spelt the way the agent spells it. Empty leaves the agent on its own default. |
| `model_by_mode` | A model per mode, such as `review: small-model`. Wins over `model` for that mode. A model typed after `--` wins over both. See [Pinning a model](../first-run.md#pinning-a-model). |
| `enabled`, `settings` | In the schema, and copied by `project new`, but no launch reads them yet. Setting them changes nothing. |

### context

| Setting | What it does |
|---|---|
| `global` | Workspace-relative paths to shared instructions, such as `global/instructions/security.md`. |
| `project` | Paths under this project, such as `context/architecture.md`. |
| `code_map` | Inlines a line per directory naming the types in it. Off by default, because it's paid for on every launch: on a mid-sized repository it costs about as much as everything else in the context together. `loadout docs find` works either way. |
| `tasks` | Puts the project's open tasks in front of every session. Off by default, and on automatically for a project registered before it had a repository. |

A file over 512KB is left out and reported, not truncated. The order these load
in is in [Context and instruction files](../context.md).

### profiles

Named slices of context, chosen with `--profile`, so that database work doesn't
drag in the frontend architecture. A profile called `default` is used when none
is named.

```yaml
profiles:
  database:
    description: Audit and schema work
    context:
      - context/database.md
    include_global: true
    agents: []                 # empty means every agent
    specialists:
      preferred: [database.postgresql]
```

`loadout storefront --profile database` launches with it. A profile's `context`
is added after the project's own; its `specialists`, when set, replace the
project's.

### environment and environments

Variables handed to the agent process. Each is a reference to your secret store
or a plain value, never a credential written out:

```yaml
environment:
  ANTHROPIC_API_KEY:
    secret: anthropic/default   # resolved from the secret store at launch
  LOG_LEVEL:
    value: debug                # a literal; never use this for a credential
  SENTRY_TOKEN:
    secret: storefront/sentry
    required: false             # launch anyway on a machine without it
```

A named environment adds its own variables and a security profile, chosen with
`--environment`:

```yaml
environments:
  production:
    description: Production investigation
    security_profile: production
    environment:
      DATABASE_URL:
        secret: storefront/production-db
```

A security profile can only tighten what the agent may do, never loosen it, and
an environment name that doesn't exist stops the launch rather than falling back
to development. See [Environments and security
profiles](../first-run.md#environments-and-security-profiles).

### specialists

| Setting | What it does |
|---|---|
| `preferred` | Specialists this project expects to be relevant, such as `database.postgresql`. A hint, not an instruction: one still loads only when the task points that way. |
| `excluded` | Specialists that must never load here. Honoured; only naming one on the command line overrides it. |
| `mode` | The mode a session starts in when none is given, such as `review`. |

You may find `is_empty: true` written under `specialists`. It's worked out from
the other three, not read, so editing it does nothing. See [Specialists and
skills](../specialists.md).

### symbols

For the code index behind `loadout docs find` and the code map, where the
built-in reading of your languages isn't right:

```yaml
symbols:
  ignore:
    - generated/**
  extensions:
    .pyw: python
  languages:
    - id: elixir
      extensions: [.ex, .exs]
      types: '^\s*defmodule\s+(?<name>[\w.]+)'
      members: '^\s*defp?\s+(?<name>\w+)'
      docs: hash
```

Run `loadout docs find` once after writing a language: a pattern that doesn't
compile drops that language rather than failing every lookup.

### launch and workspace

`launch.working_directory`, `workspace.sync_on_launch` and
`workspace.save_on_exit` are in the schema and copied by `project new`, but no
launch reads them from the project yet. Sessions start in the repository, and
syncing follows `sync-launch` and `sync-exit` in your configuration.

## If it went wrong

- **The project is on another machine but points at the wrong directory here.**
  `loadout project relocate <slug> <path>`.
- **The slug is wrong.** There's no rename. `loadout project remove <slug>
  --from-workspace` takes it out of the shared registry, on every machine, and
  then you add it again with `--slug`. Removing never deletes source code, and it
  leaves the old `projects/<slug>/` directory — memory, tasks, instructions — in
  the workspace for you to move across.
- **A context file is reported missing.** Paths in `context.project` are relative
  to the project's directory in the workspace, not to the repository.
- **A launch fails on a secret.** The reference doesn't resolve on this machine.
  Store it, or mark the binding `required: false` if only some machines need it.

## What this doesn't do

- Registering never starts an agent. Onboarding waits for a session you start,
  because that's when you've chosen to spend something.
- Nothing here changes the repository except `loadout protect`, whose hook lives
  in `.git/hooks` and is never a tracked file.
- Editing `project.yaml` isn't validated when you save it. Step 7 is the check.
- There's no command for most of the settings above. You edit the file.

## Next

[Launching a session](launching.md)
