---
id: skill.project-onboarding
kind: skill
title: Project onboarding
summary: A procedure for the first session in a newly registered project, which sets the project up and leaves what it found behind.
task_phrases:
  - 'onboard this project'
  - 'onboard the project'
  - 'set up this project'
modes:
  - 'investigate'
  - 'advise'
  - 'review'
---

## When to use

The project has an open task `onboard-project`. It was registered recently and
nothing has been worked out about it yet: no memory, settings left at their
defaults, and possibly agent files from before Loadout still sitting in the
repository. This session's job is to change that, not to start on features.

`skill.repository-review` is the procedure for learning the code. This one wraps
it with what only Loadout can tell you, and with the project's settings.

## Ask the launcher first

Everything here is a read. Run it before opening a source file, because each one
answers something you would otherwise work out the long way:

- `loadout project show <slug>` — what is registered, and where.
- `loadout instructions explain --project <slug>` — what a session here is given,
  which languages and frameworks were detected, and which specialists that chose.
- `loadout instructions audit --project <slug>` — where the code departs from what
  its own specialists ask for.
- `loadout rules budget <slug>` — what every session pays for before it starts.
- `loadout memory list <slug>` — anything already recorded.
- `loadout project survey` — agent memory on this machine that no project owns.
  Memory an agent wrote here before Loadout shows up this way.
- `loadout protect <slug> --dry-run` and `loadout migrate <slug> --dry-run` — whether
  the repository is protected, and whether agent files are committed in it.

Where one of these needs a change — importing memory, protecting, migrating — say
what it would do and leave it for the person to run. They change the repository or
the workspace, and nobody asked this session to.

## Learn the code

Follow `skill.repository-review`: the shape, the seams, the tests, one change
traced end to end, what surprised you. Then find out how the project is built and
tested, and run both. A build command that is written down but was never run is a
guess that will be copied into every later session.

## Record what stays true

With `loadout_remember`, or `loadout memory write <topic> --project <slug>`:

- How to build and test it, as commands that worked, and anything they need first.
- The decisions and traps `skill.repository-review` asks for.

Not what you did, and nothing that will be false next month. "Onboarding ran" is
not a fact about the code; the task records that.

## Propose the settings

Read `project.yaml` with `loadout_project_settings`, then propose changes with
`loadout_propose_settings` rather than editing the file: pass the whole file as it
should be, and a reason for each change. The person reviews it with `loadout
project proposal <slug>` and applies it.

Every setting in the file falls into one of three groups, and only the first is
yours to propose.

**Worth considering**, each only where what you found argues for it:

- `specialists.preferred` and `specialists.excluded`, from what `instructions explain`
  chose and what it got wrong. Confirm each id with `loadout_specialist` before
  naming it: a proposal naming a specialist that does not exist is refused.
- `specialists.mode`, when nearly all the work here is one kind: `advise`,
  `investigate`, `implement` or `review`. Empty leaves the launcher's default,
  implement.
- `specialists.style`, a named coding style such as `work`, when the person keeps
  one for this kind of work. Name only a style `loadout style list` shows. Nothing
  checks the name when you propose it. A misspelt one is accepted, and every launch
  then carries on without it. If the repository has conventions of its own worth writing down, say so
  and leave `loadout instructions new style.codebase --project <slug>` for the person.
- `context.project` and `context.global`, naming instruction files in the workspace.
  `context.project` paths are relative to the project's directory in the workspace,
  not to the repository. Name only files that exist. If a file is worth writing,
  describe what it should hold in your report.
- `context.tasks`, when the project keeps its open work in `tasks.yaml` and every
  session should see it.
- `context.code_map`, only for a codebase big enough that a session spends real
  effort finding things. It is paid for on every launch.
- `profiles`, where one part of the code — a database, a frontend — needs context
  the rest never does. A profile's `specialists` replace the project's when set.
- `symbols`, for generated code to ignore or a language the index does not read.
- `teams.done_when`, criteria every team run on this project is held to as well
  as its own, each with `text` and the `teams` it applies to (`all` for every
  one). Only what is true of every such run and checkable, such as the suite
  passing with the command that shows it. Not what one run is for.
- `name`, if the name the launcher shows is wrong.

`agents.default`, `agents.model` and `agents.model_by_mode` choose which agent and
model the person pays for. Propose them only when the person has said what they
want. Write a model name the way the agent spells it, because the launcher passes
the name through unchanged.

**Refused.** `schema_version`, `id`, `slug`, `repository`, `environment` and
`environments` cannot be proposed at all. They decide which project this is and
which credentials and sandbox a session gets. If one looks wrong, report it.

**Leave to the person.** `workspace.sync_on_launch`, set to false, stops a
launch of this project refreshing the shared workspace first. That trades fresh context
for a launch that never touches the network, which is their call, not a finding.
`aliases` adds other names the project answers to; if it needs one, say so in
your report.

Give the reason beside every change. A setting nobody can account for gets
removed by the next person to read the file.

## Done when

- The launcher's own checks were read, and what they found is reported.
- The build and the tests were run, and the commands that worked are in memory.
- Every recorded claim was checked rather than inferred.
- Settings changes are proposed, each with its reason, or none were needed.
- `loadout_task_declare onboard-project done`, with a note of what was left for the
  person to do.
