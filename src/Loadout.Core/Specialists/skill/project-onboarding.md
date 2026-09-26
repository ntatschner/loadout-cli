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
project proposal <slug>` and applies it. Identity, the repository, `environment`
and `environments` cannot be proposed at all. Consider:

- `specialists.preferred` and `specialists.excluded`, from what `instructions explain`
  chose and what it got wrong. Confirm each id with `loadout_specialist` before
  naming it: a proposal naming a specialist that does not exist is refused.
- `profiles`, where one part of the code — a database, a frontend — needs context
  the rest never does.
- `context.code_map`, only for a codebase big enough that a session spends real
  effort finding things. It is paid for on every launch.
- `symbols`, for generated code to ignore or a language the index does not read.

Give the reason beside every change. A setting nobody can account for gets
removed by the next person to read the file.

## Done when

- The launcher's own checks were read, and what they found is reported.
- The build and the tests were run, and the commands that worked are in memory.
- Every recorded claim was checked rather than inferred.
- Settings changes are proposed, each with its reason, or none were needed.
- `loadout_task_declare onboard-project done`, with a note of what was left for the
  person to do.
