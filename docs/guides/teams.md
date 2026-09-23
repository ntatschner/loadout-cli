# Running a team

**At the end:** a team run started on a goal you wrote, held to criteria you
agreed, and a list of what it delivered.

## Before you start

- A registered project with your agent working in it. See
  [launching a session](launching.md).
- A goal a single session would struggle with. A team is for work that wants
  separate sessions — separate budgets, permissions and branches, and a record of
  who did what. For anything smaller, your agent's own subagents are cheaper.

## Steps

### 1. See which teams you have

```sh
loadout team list
```

<!-- capture: docs/captures/team-list.txt — the teams available on this machine. -->

You should see each team with where it came from: shipped, from a pack, or
written by you. `iterating-project` plans, implements, reviews and verifies in
rounds. `bug-hunt` reproduces a bug, proves its cause and fixes it.
`docs-crew` finds where the documentation and the code disagree. The
[teams reference](../teams.md) describes each one.

### 2. Look at one before you run it

```sh
loadout team show bug-hunt
```

You should see its nodes, their roles, the rules a run follows, its budget, and
anything that would stop it running here.

### 3. Start a run, saying what done means

```sh
loadout team run docs-crew "make the docs true" \
  --done-when "every command in docs/commands.md exists" \
  --done-when "the suite passes on a clean checkout" \
  --done-when "the changelog names the change"
```

Give `--done-when` once per criterion. Every node is told them, and the lead's
final report has to give a verdict on each: met, unmet or not attempted.

If you give none, the lead proposes criteria and they're put in front of you
before any worker starts. Change them, accept them, or empty the box to run
with nothing checking it.

A run with no budget in its team file and no `--rounds` is refused before it
starts, naming both — without either, a lead that keeps asking for one more
thing would spend until somebody noticed. Every team that ships sets a budget.

### 4. Choose how much it asks you

`--autonomy` sets the posture:

- `supervised` — the default. The run goes on, and holds for you at the gates
  the team names.
- `manual` — asks you at every step. It refuses to start where nobody can
  answer, such as from a schedule.
- `autonomous` — holds for nothing, within its budget and permissions.

A team file can only set its outward gates — anything that leaves the machine —
to `ask`. What an autonomous run may push is agreed on this machine, not in the
team file, because the team file is shared and anybody can edit it.

### 5. Watch it

```sh
loadout team status
```

<!-- capture: docs/captures/team-status.txt — a team run's progress, with two of three criteria met, written as words. -->

You should see each node, what it's doing and what it has cost, and then the
criteria. For example, part way through a run:

```text
  Done when 2 of 3 met
  + met           every command in docs/commands.md exists
      docs-auditor/1 checked all 159
  + met           the suite passes
      verifier/1 reported 2843 passing
  ! not attempted the changelog mentions it
```

The same account is in the launcher under **Tools → Team runs…** and on the
[dashboard](dashboard.md). All three read one journal the run writes, so they
can't disagree.

![The launcher's team runs screen, showing one docs-crew run in progress on storefront, with its round, its nodes and what it has spent.](../images/team-runs.svg)

### 6. Answer it when it asks

```sh
loadout team gate
```

You should see what the run is waiting on and the command to answer it. To
steer the lead without interrupting a node, send a message; it's read at the
start of the lead's next round:

```sh
loadout team message 20260918-1436-ed59 --message "leave the tests alone"
```

### 7. See what it delivered

```sh
loadout team outbox
```

You should see the commits the nodes reported, with the files inside each and
the node that made it, and a second block for anything written but not
committed, such as a plan.

## How a run stops

- The lead says it's done, and every criterion has a verdict.
- The budget is spent. A cap stops the run after the turn that crosses it,
  because an agent reports a turn's cost when the turn is over.
- Two rounds pass without progress.
- You stop it, with `loadout team halt`.

## If it went wrong

- **The run was refused.** Read the reason: usually no budget and no
  `--rounds`, or an autonomous run asking for an outward action this machine
  hasn't agreed to.
- **A `done` was sent back.** A criterion was unmet or unanswered. The reason
  names it.
- **You want the detail.** `loadout team log --events` prints what happened —
  rounds, launches, asks, reports, gates — without every node's commentary.

## What this doesn't do

- It's not a second way to run an agent. Each node is an ordinary Loadout
  launch, with the same instructions and screening.
- With no criteria, "done" is the lead's word for it.
- An outward action a team file names does nothing until this machine agrees to
  it, with `loadout config set team-outward-allowed`. The
  [teams reference](../teams.md) has the detail.

## Next

[Using the dashboard](dashboard.md)
