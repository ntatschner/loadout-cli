# Teams

A team is several agents working one goal, with one of them reporting back to
you. You give it a goal in your own words; a lead splits that into requests, the
workers do the work in their own sessions, and the lead reads what came back and
decides what to ask for next. When it is done — or out of budget, or getting
nowhere — you get one report.

```bash
loadout team list
loadout team run iterating-project "make the config loader handle a missing file"
loadout team status
```

That is the whole of the everyday use. The rest of this page is what is
underneath it, and how to write one of your own.

## What it is not

It is not a second way to run an agent. A node *is* a Loadout launch: the same
project, the same compiled instructions, the same security profile, the same
transcript. What a team adds is who asks whom, and what the run is allowed to
spend before it stops.

It is also not a replacement for an agent's own subagents. Claude Code fanning
work out inside one session is cheaper and keeps everything in one context; a
team is for work that wants separate sessions — separate budgets, separate
permissions, separate branches, and a record of who did what.

## The teams that ship

| Team | What a run of it does |
| --- | --- |
| `iterating-project` | Plans, implements, reviews and verifies in rounds until the goal is met, the budget is spent, or two rounds make no progress |
| `bug-hunt` | Reproduces a bug without a person watching, proves its cause, fixes the proven cause, and proves the fix holds |
| `release-crew` | Checks the tree is fit to release, writes the notes, and tags. The push is a gate unless the run is autonomous |
| `docs-crew` | Finds where the documentation and the code disagree, fixes it in the voice the docs already have, and follows the changed pages as a new reader would |
| `dependency-sweep` | Updates dependencies one branch per bump, in parallel, each verified with the suite green before it is offered for merge |
| `marketing-studio` | Turns a goal into a strategy, writes the pieces, and edits every claim against the facts. **Nothing is sent, in any mode** |
| `product-company` | A shape to copy, not a team to run: an executive lead, department leads, and workers under each |

`loadout team show <team>` prints one in full — its nodes, their roles, the
rules a run follows, and anything that would stop it running here.

## How a run behaves

**Rounds.** The lead gets your goal and reports back with requests. Each round
is: brief the nodes it asked for, run them, read what they wrote, hand it to the
lead. `--rounds` caps how many times it may come back; the default is five.

**Nodes that may run together do.** A node says how many instances may run at
once, and whether each gets its own git worktree. Briefing stays sequential,
because you cannot answer two gates at once, and so does starting, because two
`git worktree add` calls at once write one index.

**A branch that conflicts goes back to whoever wrote it**, under the same
instance name so it lands in the same worktree, told the branch, the target and
the files, and forbidden to touch the target or merge anywhere. It is retried
once and never again.

**A run that gets nowhere stops.** Two rounds without a request is no progress,
and no progress is a stop condition like any other.

## The three postures

`--autonomy`, or the team's own setting when you do not pass one.

- **manual** asks you at every step. It refuses to start where nobody can
  answer — down a pipe, in CI, from a schedule — because a run answering its own
  gates would be autonomous wearing manual's name.
- **supervised** is the default: the run proceeds, and holds for you at the
  gates the team names.
- **autonomous** holds for nothing, within its budget and its permissions. It is
  the only posture where a team's named outward actions apply — and then only
  the ones this machine has agreed to. In manual and supervised runs that list
  is ignored entirely.

## What a run may spend

Three currencies, all optional, all in the team file:

```yaml
rules:
  budget:
    usd: 25
    turns_per_node: 40
    wall_clock: 4h
  stop_when: [goal_met, budget_spent, no_progress_2_rounds]
```

Money is per run; turns are per node session. A cap stops the run *after* the
turn that crosses it — the agent reports a turn's cost when the turn is over, so
there is no earlier moment to stop at.

## What a team may not do

A team file is shared: it lives in your workspace and anybody on your team can
edit it. So it may ask for things and it may never grant them.

- **`gates.outward` may only be `ask`.** A team file setting it to anything else
  is a finding, and `team show` says so. What a team *may* name is a list of
  outward actions allowed in autonomous runs — and that list is ignored in every
  other posture.
- **That list is a request, and this machine answers it.** Nothing in it applies
  until you agree to it here:

  ```sh
  loadout config set team-outward-allowed "git push --tags"
  ```

  Unset means none, which is why the shipped `release-crew` — which asks to push
  a tag unattended — will not do so on a machine that has not said it may. An
  autonomous run of a team asking for something this machine has not granted is
  refused before anything starts, naming the action and both ways out. Matching
  is exact: agreeing to `git push` does not agree to `git push --force`.
- **Each node's permissions come from its role**, not from the team file. They
  are written out before the run starts, and deny wins.
- **What no rule covers is put to you**, if you are there to answer — the
  question names the node, its role and what the call is pointed at. You are
  never asked about something a rule already settled, either way: a deny you
  could be asked past would make every deny list a suggestion. Where nobody is
  watching — an autonomous run, or any run down a pipe — nodes are not told they
  may ask, and what no rule covers is refused as before. A question nobody
  answers within five minutes is refused too, and says that is why rather than
  blaming the role.
- **A worker's settings are screened** exactly as a launch's are, by the same
  code: a node cannot be given a setting a launch could not.

This is the same split as command policy and as specialist packs: the shared
half proposes, and the local half decides.

## Writing one

A team is a YAML file — in `projects/<slug>/teams/` for one project, or
`global/teams/` for all of them.

```yaml
name: quick-review
description: Reads a change and says what is wrong with it.
lead: lead
nodes:
  lead:
    role: role.project-lead
    delegates: [reviewer]
  reviewer:
    role: role.reviewer
    model: haiku
rules:
  autonomy: supervised
  gates:
    outward: ask
```

Per node: `role` (a specialist of `kind: role`), `agent`, `model`, `delegates`,
`worktree`, `parallel`, and `parameters` for a role that takes them. The lead is
the node that reports to you and must be one of the nodes.

`loadout team show quick-review` checks it. A team naming a role that does not
exist, a lead that is not a node, or a gate nobody can decide is a finding
there, in a sentence saying what to change — rather than a failure half way
through a run.

**Per-node models are the lever worth reaching for.** The lead is the node whose
model matters most: it is the one applying your project's memory and deciding
what to ask for. A reviewer reading one diff and a release validator running
every check are both reviewing, and only one of them is worth a large model.

### Where teams come from

Built-in, then approved packs, then your workspace, then the project — later
replacing earlier by name, whole. A pack may replace a team that ships, because
that is what approving it agreed to; nothing can take back a team you wrote
yourself. `team list` says where each one came from.

A pack carrying teams goes through the same gate as one carrying specialists: it
is pinned to a commit, and somebody on this machine approves that commit having
read it. See [Specialists and skills](specialists.md).

## Watching a run

Three views of one thing. Each run writes one journal, and all three read it, so
there is one account of what happened rather than three that can disagree.

- `loadout team status` — where each node got to, what it is doing now, what it
  cost.
- `loadout team dashboard` — a page on this machine, live, at a loopback address
  behind a token that changes every start. It reads and never writes.
- **Tools → Team runs…** in the launcher — the same, in the terminal UI. See
  [The launcher](launcher.md).

`loadout team runs` lists what has run; `loadout team log` prints everything one
wrote down, and `--follow` keeps reading as it writes. Both `status` and `log`
take a run identifier and use the most recent one when you leave it out.

## Runs that start without you

```bash
loadout team schedule add docs-crew "check the docs against the code" --every 24h
loadout team schedule add bug-hunt "look at what the suite is failing" --on commit
loadout team daemon
```

Nothing fires unless the daemon is running, and `doctor` tells you when you have
schedules and nothing firing them — the case a restarted machine looks exactly
like. To stop that happening:

```sh
loadout team autostart enable     # --dry-run first says which file it would write
loadout team autostart show
loadout team autostart disable
```

Per user, never for the machine, so it needs no administrator rights and can
always be undone by whoever set it. On Windows it is a shortcut in your Startup
folder, started minimised — the daemon is a console process you stop with
Ctrl+C, so it needs a window, but not one that takes focus at every login. On
macOS it is a launch agent, on other Unixes a desktop entry under
`~/.config/autostart`; **neither of those has been logged into**, only the files
they write are covered.

It records the launcher as it was invoked, so run it again after updating
Loadout if the launcher moved.

A schedule is machine-local. It may not name a manual team, nothing may repeat
faster than five minutes, and a missed one is not made up for: a run that should
have happened at three in the morning does not all fire at once when you open
the laptop at nine.

`--on commit` fires when the project's HEAD has moved since the daemon last
looked. The first look never fires — it only records where the repository is —
and a dry run records nothing, so previewing a trigger cannot arm it.

## Runs started from outside

Off until you turn it on, and then off again for anything you have not named.

```sh
loadout team webhook enable            # makes a token, prints it once
loadout config set team-webhook-teams "docs-crew"
loadout team daemon
```

Two separate acts on purpose. The token says **who** may ask; the teams list
says **what** they may ask for. A token on its own starts nothing, because the
mistake worth designing against is turning the webhook on to try it and
forgetting that you did.

```sh
curl -X POST http://127.0.0.1:8321/api/trigger/docs-crew   -H "X-Loadout-Token: <your token>"   -d '{"goal":"check the docs against the code","project":"loadout-cli"}'
```

It answers as soon as the run has started, not when it finishes — a caller
waiting for a team run would hold a request open for minutes, and the run is
watchable by every other means here. Team names match the way you would type
them, allowing for case and stray spaces, but never by prefix: naming
`docs-crew` does not name `docs-crew-extra`.

The token lives in your operating system's credential store, never in a file,
and is shown once. Nothing reads it back out afterwards — one that can be
re-read is one in every screenshot of the machine it is on. Lost it, run
`enable` again; the old one stops working immediately.
`loadout team webhook show` says whether there is one and which teams it may
start, and never what it is. `loadout team webhook disable` forgets it, which
refuses every trigger whatever the teams list still says.

**Reaching it from another machine is a separate decision again.** The server
binds loopback until you say otherwise:

```sh
loadout config set team-webhook-listen "0.0.0.0"
```

On Windows a non-loopback binding needs a URL reservation, and the daemon says
so with the `netsh` line to run if it cannot bind. Nothing here is hardened for
a hostile network: the token is compared in fixed time and that is the whole of
it. Put it behind something that is, or leave it on loopback and let a local git
hook be what calls it.

## Previewing

`--dry-run` on a run says what it would do — which nodes, on which agent, with
which model, in which worktree — and does none of it. No session is started, no
worktree is made, no branch is written, and nothing is recorded.

## What is not built

Said here rather than discovered:

- Nodes cannot declare tasks of their own; one task is declared per run.
- The machine's ceiling covers what a *webhook* may start and what a team may
  allow. It does not cover everything else a node may do.
- The machine's ceiling covers outward actions only. Everything else a node may
  do comes from its role and the agent's own permissions, checked when it tries
  rather than before the run starts.

## See also

- [The launcher](launcher.md) — watching a run from the terminal UI
- [Specialists and skills](specialists.md) — roles are specialists, and packs
- [First run and configuration](first-run.md) — the security profiles a team runs under
- [Commands](commands.md) — the whole command surface
