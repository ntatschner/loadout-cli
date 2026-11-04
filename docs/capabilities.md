# Capabilities and offers

Say you add a task to write release notes, on a project with no CI and no
release workflow. Left alone, an unattended team run picks that task up and
builds on something that isn't there. This page is about how Loadout notices,
what it offers you instead, and what it will and won't do without asking.

There are three parts. Every idea and task has a type. Every project has a
profile of what it already has. And when the two meet a gap, you're offered one
prerequisite task, joined to the waiting work by a link that team queues respect.

## Types

A type is one of seven: `new-project`, `feature`, `bug-fix`, `ci-cd`, `release`,
`docs` and `maintenance`. It's a closed list because checks hang off it, and a
check can only be written against a word every surface agrees on. Your own
labels sit beside it and can say anything, because nothing reads them but you.

A rule guesses the type from the words when the item is captured. The agent
refining an idea can revise that guess. Once you choose a type yourself, neither
touches it until you pick "let the agent decide".

## The profile

```bash
loadout project capabilities my-project
loadout project capabilities my-project --refresh
```

Each check reports one of six states:

| State | What it means | Can it make an offer? |
|---|---|---|
| `present` | It's there, and does the work in this repository | No |
| `delegated` | It's there, and calls a shared workflow | No |
| `manual-release` | Releases are published, but no workflow publishes them | Yes, without holding anything |
| `gap` | Looked for, and not there | Yes, if a template fills it |
| `unknown` | Couldn't be looked at: no token, offline, or too slow | Never |
| `no-permission` | GitHub refused, and the line names the permission it wanted | Never |

The last two are the rule the rest of this rests on. **The profile never claims
a gap it hasn't seen.** A missing token can't make your work wait, because a
check nobody could look at is not a gap. A GitHub token comes from `gh auth token`
where the GitHub CLI is signed in, and from `GITHUB_TOKEN` otherwise. It isn't
written anywhere.

Plain `project capabilities` shows the profile last kept and how old it is, and
doesn't reach GitHub, so it's instant and works offline. The kept copy is only
ever for showing. Anything that decides something builds the profile again
first: accepting an offer, a team run starting for a task, a queue pass.

A queue pass shares one look per project across five minutes, because a pass can
start many runs and each look asks GitHub several questions. A project with no
open prerequisite isn't looked at at all.

The profile, your answers and the environment names you've given live in the
workspace, next to the project's other records. Nothing goes in the repository.

## Offers

```bash
loadout task offers release-notes --project my-project
loadout task offers release-notes --project my-project --accept ci
```

A type picks which checks matter: `ci-cd` raises CI, environments and branch
protection, and `release` raises CI, release and environments. `docs`,
`maintenance`, `feature`, `bug-fix` and `new-project` raise none. A done-when
that only CI can meet raises CI on any type, because that's your own words saying
the work depends on it.

In Studio these appear as the **Before this** band on an idea, one line a gap,
with a single choice on each line. Press `o` to take the first offer and `s` to
change which stack it's written for. In a terminal, `task declare` prints the gap
and the command for it, and creates nothing. A link is only ever made by somebody
choosing to make it.

The terminal launcher's Ideas screen has the same line for each gap: `o` takes the
first offer, `s` changes its stack and `w` says it isn't needed.

Only three templates make offers: CI, release and environments. Each offer can
hold unattended work, so that power stays with text Loadout ships and tests.
Branch protection shows up as a gap with no offer, and you can link a task of
your own to it by hand.

### What accepting makes

One task, with the template's text copied into it, and the waiting work made to
require it. If an open prerequisite for that check already exists, the work
waits for that one instead of a second being made.

The text is frozen. A later Loadout that ships better wording doesn't change a
task you've already accepted, so nothing an unattended run reads moves underneath
it. `project capabilities` lists an open prerequisite made from an older template,
or for stacks that no longer match the repository, and `--renew <task>` rewrites
it. A renewal is refused while a run owns the task. It records the old and new
versions, and if it moves a release task to another stack it clears the ticked
secrets step, because a tick about one stack's secret says nothing about another's.

| Template | Task | Written for | Step for a person |
|---|---|---|---|
| CI | `add-ci` | One job per stack detected | None |
| Release | `add-release-workflow` | The one stack you pick | Setting the secret it names |
| Environments | `add-environments` | The names you give, `dev` and `production` to start with | Creating them on GitHub |

Stacks come from manifests such as `global.json` and `package.json`. CI defaults
to every stack it found. Release defaults to the stack you last chose if it's
still there, otherwise the one nearest the root. Both can fall back to generic
text. Your environment names and release stack are remembered for the project,
so the common case stays one key.

## When a prerequisite closes

A link to a task is met when that task is done. A prerequisite made from a
template can also close by itself, when a fresh profile sees what it was for. The
history line gives the check, the date and the evidence: `ci seen 2026-11-03:
build.yml runs on push or pull request.`

| Check | What closes it |
|---|---|
| CI | The profile finding a workflow that runs on push or pull request |
| Release | The profile finding a release workflow **and** you having ticked the person's step |
| Environments | GitHub listing them, which ticks the step as well |

Release asks for both because a workflow file doesn't prove the secret exists, and
your tick is the only evidence there is. Releases published by hand don't close it
either: the work was waiting on automation. Environments differ because what GitHub
lists is exactly what the step creates.

```bash
loadout project capabilities my-project --tick add-release-workflow
```

If a run is working on the prerequisite when the capability appears, the work
waiting on it is released straight away and the run keeps its task. The task
closes the next time the profile is built after the run has ended.

## Saying you don't need it

```bash
loadout project capabilities my-project --not-needed environments
loadout project capabilities my-project --needed environments
```

"Not needed" lasts for the project. It stops the offer, and it's listed by
`project capabilities` so it can be undone. If an open prerequisite is holding
work for that check, you're asked before its links come off, or you can say so with
`--release-links`. Without that, the links stay: releasing work quietly is the
failure they exist to prevent. `--needed` lists the open work the gap touches
again, and still creates nothing.

Marking a capability not needed never closes anything else, and neither does
leaving an offer for later, which remembers nothing at all.

## When the type changes

Changing an item's type can leave a link the new type doesn't need: a release task
retyped as a bug fix still waits for its release workflow. `idea type <id> <type>
--release-links` takes those links off in the same step, and without it you're asked.
Studio has a box beside the type picker, ticked to start with, that does the same.
Only the link goes. The prerequisite stays, because other work may wait for it, and a
link you made by hand is never touched. Handing a type back to the agent changes
nothing, since it says nothing about what the work depends on.

## While a run is going

If a check that was there when a run started has gone by the time it ends, the
run's report says so, and so does its timeline. That's all it does. Pausing a run
over something it may not even touch costs more than the gap, and the next run to
start looks again. This compares the repository's files, so it notices a deleted
workflow, not an environment removed on GitHub.

## New projects

An idea typed `new-project` has no repository to look at. Accepting it with
`--new-project` sets up all three templates in generic text, each required by the
task, since a new project is missing everything by definition. Leave one out with
`--skip-setup <check>`, and it's remembered as not needed. Once the project has a
repository, the same rules close these as any other.

## Checks of your own

A specialist can add a check, or change one, in its frontmatter:

```yaml
checks:
  - id: code-owners
    name: A code owners file
    types: [ci-cd]
    files: [".github/CODEOWNERS", "CODEOWNERS"]
```

A check is either `files`, globs from the repository's root, or `github`, naming
`environments`, `branch-protection` or `releases`. It never runs a command,
because the profile can hold and release work, so it has to stay fast and free of
side effects. A walk that couldn't read every folder reports `unknown`, not a gap.

A specialist's gap counts on the dashboard, marked as having no offer, and you can
link a task of your own to it by hand. It can't name a template.

A specialist from your workspace can override a built-in check by using its id, or
give only `types` and `name` to change which work it matters to and leave how it's
decided alone. An overridden `ci`, `release` or `environments` still offers
Loadout's template when it reports a gap, because the text is still ours. Two of
your specialists declaring one id makes that check `unknown`, with both named, so
a clash can't hold work. A check that isn't valid is left out and reported by the
specialist checks.

The four built-in checks live in Loadout's code, not in a shipped specialist.

## Links by hand

```bash
loadout task requires release-notes other-project/add-api
loadout task requires release-notes other-project/add-api --remove
```

A link made by hand can name a task on any list, and is released only when that
task is done. `task requires --remove` takes off any link, including one an offer
made. See [Teams](teams.md) for how a queue treats a held task.

## What this does not do

- It only knows GitHub. A project hosted elsewhere shows its GitHub-only checks as
  `unknown`, and gets no offer for them.
- It reads workflow triggers, not whether a workflow works. A CI file that always
  fails counts as CI. In a repository with several stacks, CI counts as present if
  any workflow runs, and a stack no workflow seems to build is only ever a note,
  because that's a guess from the workflow's text.
- It can't see secrets, which is why release has a step for a person.
- Release can be offered for one stack per project.
- A run's ownership of a prerequisite is read from the run's journal, so a run that
  died without ending keeps its task open until the run is closed. The waiting work
  has been released by then, so nothing is stuck behind it.
