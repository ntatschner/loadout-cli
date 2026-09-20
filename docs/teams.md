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

## Which tree it works on

A run works on the **project's registered path**, not on wherever you typed the
command. Those are often the same and sometimes not — a git worktree of the same
repository, on a different branch, is the case that catches people.

So a run says where it is going before it spends anything:

```
Working on loadout-cli at D:\gitilauncher on docs-features-and-guides
You are in D:\gitilauncher-teams, which is not where this will run.
```

The second line only appears when they differ. The branch is the part worth
reading: a path you half-recognise looks right, and a branch you are not on
looks wrong at a glance.

Every run records which project and path it used, so `team status`, the
dashboard and the launcher's screen can all say it afterwards. Runs recorded
before that was written down simply do not show it.

## What a team is for, and how it always works

A run has a goal: the thing you typed when you started it, true of that run and
no other. A team has one too, and it is a different thing.

```yaml
name: system-watch
description: Investigates a system problem, proves the cause, fixes it, and turns the fix into something the team keeps and reuses.

goal: >
  Keep this system working, and get better at it every time. A fix that only
  ever existed in one run's transcript is a fix this team will work out again
  next month.

declarations:
  - When a resolution to a problem has been found and shown to work, write it as
    a script or a function rather than leaving it as steps in a report.
  - Register anything you write in the team's directory, with a name that says
    what problem it solves, so every later run of this team can find it.
  - Before working out a fix, look in the team's directory for one that already
    exists. Say in your report whether you found one and whether it worked.
```

**`goal`** is what the team exists for, standing. Every node reads it above its
own task, because a node given a narrow job still needs to know what the team is
for: *find why the disk filled* is a different job inside a team that exists to
keep a system up from inside one that exists to write a report about it.

**`declarations`** are the rules every node follows whatever the run is about —
how this team works, rather than what it is doing today. Write each one so
somebody could check a node against it afterwards, which is the bar a done-when
condition is held to. A declaration nobody can check is a hope.

Both are optional, and most teams need neither: a description that says enough
does not need saying twice, and an empty heading in a brief reads as something
missing.

A declaration **cannot raise what a node may do**. It is prose in a brief, read
by a model. What a node is permitted lives in the machine's own configuration
and is decided before anything starts — that is the same rule the whole of this
file works under, and nothing a team writes about itself changes it.

### The team's directory

The first declaration anybody writes needs somewhere to put things, so a team
has a directory of its own:

```
<state>/teams/work/<team>/
```

The same path for every node of every run of that team, made before the first
node starts, and named on every brief. Left to each node, *the team's directory*
is a phrase that resolves differently every time and the work is lost between
runs — which is the whole thing the example above is trying to stop.

It sits beside the runs rather than inside one, because that is the point: a
run's directory goes when the run is over, and a team that works out how to fix
something should not have to work it out again next week.

It is **not in the repository**. What a team learns about keeping a system up is
not a change to whatever it happened to be looking at, and committing it into
somebody's project because a declaration said "register it" would be a surprise.

Being outside the repository is also why the launch has to hand it to the agent
explicitly: a node may only write where it has been told it can. For a while it
was not handed over, so every node was briefed with a path it could not write
to — the directory existed, the path was right, the declaration was clear, and
`Write` refused. It is passed now, and a test asserts the launch carries it,
because nothing asserted that before and that is how it went unnoticed.

`--dry-run` says where it would be and creates nothing.

Two things are checked when a team file is read, because both are invisible
until a run is paying for them: an empty declaration (it would reach every brief
of every run as a bare dash) and a goal over 600 characters (every node reads it
every round).

`loadout team show <team>` prints the goal and the declarations above the nodes,
because a standing goal frames everything under it and reading the nodes first
is reading them without it.

## Automated remediation

A team whose declarations tell it to write its fixes down accumulates a shelf of
scripts in its directory. Letting one of them run on your machine with nobody
watching is a decision somebody has to make deliberately — so it takes **two
keys, and neither of them is an agent's**.

A node can register a remedy, improve it, and write that it is wonderful. It
cannot trust it.

### A remedy

A script, and a record beside it saying what it is. Every node's brief says this
shape, because how you register one is a fact about how Loadout works rather
than something each team should have to restate in a declaration — the first
real run of `system-watch` proved why. Told only to "register it in the team's
directory", the reproducer wrote the script and a `README.md` index, which is a
perfectly fair reading, and `team remedies` reported an empty shelf about a
directory with a good script in it.

```yaml
# <team directory>/remedies/clear-build-cache.yaml
name: clear-build-cache
kind: disk
what: Deletes build output older than fourteen days from D:\builds.
assumes: The build box, PowerShell 7, D:\builds exists, nothing is mid-build.
proves: Free space before and after, and a build still succeeds afterwards.
script: clear-build-cache.ps1
registered_by: 20260920-1200-abcd
revision: 2
```

**Nothing in this file decides anything about trust.** It lives in the team's
directory, the team's nodes are told where that is and told to put things in
it, and `role.fixer` has unrestricted `Write`. A record kept here is a record
an agent can write — so this is what the remedy *is*, and whether it may run is
decided elsewhere.

That was not the first design. Trust used to be a field in this file, and a
node could set `trust: trusted`, compute the fingerprint over its own script,
and Loadout said *runs unasked*. A record that still claims trust is shown for
what it is rather than quietly ignored.

A record beside the script rather than front matter inside it, because the
scripts are in whatever language suits the problem and a comment convention
that had to work in PowerShell, bash and Python would be three conventions.

**`kind`** is the classification a machine sets a rule against — free text,
because the kinds worth telling apart are the ones a particular system has.

### The first key: this machine, by kind

```sh
loadout config set team-remediation "disk=trusted, service=ask, network=never"
```

| Rule | What it means |
| --- | --- |
| `never` | Refused outright, however trusted the remedy is. |
| `ask` | Held for you, every time, trusted or not. |
| `trusted` | May run unattended — **if** somebody has trusted that exact script. |

By kind rather than one switch for "automated remediation", because *may it
restart a service unattended* and *may it delete files off a full disk* are
different questions with different answers.

A kind nobody has written a rule for is **asked about**, not refused. A
remediator that cannot ask is one that stops, and a person who is never asked
never learns the kind exists to write a rule for it.

### The second key: you, about one script

```sh
loadout team remedies --team system-watch
loadout team remedy show clear-build-cache --team system-watch --script
loadout team remedy trust clear-build-cache --team system-watch
loadout team remedy trust clear-build-cache --team system-watch --revoke
```

This writes to **your machine's own configuration**, not to the remedy's
record — beside `team-remediation`, behind the same boundary as every other
decision this machine makes, and nowhere a team's nodes are told to write:

```yaml
teams:
  trusted_remedies:
    - team: system-watch
      remedy: clear-build-cache
      fingerprint: 5089960f...
      by: nigel
      at: 2026-09-20T19:03:39Z
```

It records a **fingerprint of the script as it is now**, and a remedy that no
longer matches asks again. That matters more than it sounds: the declaration
telling a team to keep improving what it registers is exactly the thing that
would otherwise carry one script's trust onto another — and the second script is
the one nobody read.

A remedy trusted without a recorded fingerprint is asked about too. The safe
reading of *I cannot tell whether this is what you agreed to* is to ask.

### Who can run one

Exactly one role: **`role.remediator`**. Every other role in the library has
`Bash(git …)` and nothing else, so it cannot execute a script at all — which
means that until this role existed the whole harness gated something nothing
could attempt.

What makes a shell acceptable for that one role is precisely that it does not
decide which scripts may run. This machine does, per remedy, before the node
starts. Its rules say so, including that it must not write a remedy and run it
in the same turn: nothing it writes has been agreed to, and running it would be
deciding that for itself. The destructive shells are denied outright, so "may
run a remedy" never quietly becomes "may run anything".

### Unattended runs never wait

A held remedy is a question, and a question needs somebody. On a run with
nobody watching — autonomous, or down a pipe — a held remedy is **refused**
rather than queued, and the node reports it as a blocker.

That is deliberate and it is the same rule every permission here follows: a
question nobody answers leaves a node sitting still and spending until it gives
up, which is worse than the refusal it would have had at once.

So an overnight run either runs remedies that were already agreed to, or it
stops and tells you. It never sits waiting. If you want to be asked, something
has to be watching — a terminal, or the daemon serving its dashboard.

### What is waiting on you

```sh
loadout team remedy requests --team system-watch
loadout team remedy requests --team system-watch --approve r1 --reason "read it, disk is urgent"
loadout team remedy requests --team system-watch --refuse r1
```

Each one says which node asked, in which run, what it says the problem is, and
**why it is being held rather than run** — which is the part somebody deciding
needs, and is a sentence rather than a code. Refusing is not a dead end: the
node reports what it needed and why, rather than finding another way round.

### What a node actually runs into

The ruling is not advice. It is applied where a node's tool calls are already
answered, so a remediator that reaches for a registered remedy meets it:

- **runs** — allowed, with no question, and the reason recorded.
- **asks** — held for a person, saying which remedy and why it is being held.
- **refused** — refused outright, and *not* put to anybody: a machine that said
  `never` is not asked again.

One rule decides how that composes with everything else, and it is the rule the
whole of teams follows: **a remedy can only ever make things stricter.**

- A role that **forbids** Bash still forbids it. Deny is checked first, and a
  trusted remedy that got past a deny list would make every deny list a
  suggestion.
- A role that **never allowed** Bash is not given it by trust. If a remedy could
  widen a role, it would be a file an agent writes deciding what an agent may
  do.
- A role that **does** allow the call is where a remedy can still hold it or
  refuse it.

A remedy is recognised by its script's name appearing in the call, not by path:
the same script is reached by an absolute path, a relative one and a shell
variable, and a gate that only caught the first is one you walk round by typing
`cd`. A name that turns up in an unrelated command is a false match, and the
cost of one is being asked about something that did not need it — which is the
right way round.

Every ruling is resolved **before the node starts** and written into its policy,
so the thing answering its questions reads no files and holds no opinion.

**What this does not do.** It is a decision record for *registered* remedies,
not a sandbox. A node whose role allows Bash can still run a script it has just
written, and nothing here stops it — what stops it is the role's own tool
policy, which is the boundary that has always done that job.

Nor is any of this proof against a node with *unrestricted* shell access.
Trust sits in `machines.yaml`, which is a file on the same machine under the
same user, and a node that could run arbitrary commands could edit it. Moving
trust there removes the case that actually existed — a node writing its own
trust into a directory it was **told to write in** — and leaves trust exactly
as well protected as every other decision this machine makes. The thing keeping
a node away from `machines.yaml` is its role's tool policy, and that is the
same answer as everywhere else here. A remedy
registered during the run it was written in is not in the policy, and that is
the right answer rather than a gap: nobody has trusted it, so the most it could
ever be is *ask*.

### What it comes to

| Remedy | Machine | Result |
| --- | --- | --- |
| untrusted | `trusted` | asks — nobody has read this one |
| trusted | `ask` | asks — this machine holds that kind |
| trusted | `trusted` | **runs unattended** |
| trusted, then changed | `trusted` | asks — the trust was for the old script |
| trusted | `never` | refused |

Only one row runs anything on its own.

## Watching a run

Three views of one thing. Each run writes one journal, and all three read it, so
there is one account of what happened rather than three that can disagree.

- `loadout team status` — where each node got to, what it is doing now, what it
  cost.
- `loadout team dashboard` — a page on this machine, live, at a loopback address
  behind a token that changes every start. It watches and it acts: starting a
  team, answering a gate, holding a run, stopping one, renaming its room,
  opening a pull request, sending the lead a message and typing at a live node
  are all buttons on it.
  Nothing there implements any of that — each button runs the command you would
  have typed, so there is one behaviour rather than two that drift.

  That sentence was true of the daemon and not of this command until 20
  September 2026. The page drew every one of those controls and `team dashboard`
  answered all of them with "this server only reads", which is worse than not
  drawing them.

  A team started from `team dashboard` runs **inside that command**, so it stops
  when the window does. The daemon is meant to stay up and its runs outlive the
  browser. The page cannot tell the two apart, so the server says which it is
  and the form writes it down above itself — and so does the terminal, when it
  starts.

  `loadout team dashboard --watch-only` is the old behaviour, for a screen in a
  corner: the server refuses anything that would change a run or start one, the
  page is told so, and it puts the controls away and hides the form rather than
  leaving buttons that do nothing.
- **Tools → Team runs…** in the launcher — the same, in the terminal UI. See
  [The launcher](launcher.md).

`loadout team runs` lists what has run; `loadout team log` prints everything one
wrote down, and `--follow` keeps reading as it writes.

`loadout team log --events` prints only what happened. A node writes a line for
every tool call it makes and every sentence it says about itself, and on one
four-minute run that was 42 of its 81 events - so the log answered "what did
each node do, minute by minute" long before it answered "what happened in this
run", which is the question it gets asked first. `--events` drops that
commentary and leaves the spine: rounds, launches, asks and answers, reports,
gates, merges, ends. Nothing is filtered out of the journal or out of `--json`,
and the full account is still the default.

Two things that used to be missing from it are now on the line. A node stopping
to ask a person for permission, and the answer it got, printed as the bare words
`node.asked` and `node.answered` - the most consequential moment in a run, and
the least legible line in its log. And a run's last line said why it stopped but
not how many rounds it took or what it spent, both of which it had already
recorded.

Permission decisions are read out of a side file at the end of a node's turn, so
until now they were dated at the *fold* rather than at the decision. In one real
run that put two of them three minutes late, below the line saying the node had
ended. Runs from here on record the time the decision was made; journals already
written keep the time they were folded, because that is what they say.

### Saying something to a run

Two different things, with two different answers, because they are two
different risks.

**To the lead** — the box in the detail pane, or `loadout team message <run>
--message "..."`. It is read at the start of the lead's next round, which is
the point: it steers the run without interrupting a node mid-thought. The
dashboard's own token is enough.

```sh
loadout team message 20260918-1436-ed59 --message "leave the tests alone"
```

**To a node, now** — the box under whichever node's output you are reading, or
`loadout team say <run> --node <node> --message "..."`. It goes into a process
that is running with your file access, at a moment nobody chose, so it needs a
**second credential** that the dashboard's token does not grant:

```sh
loadout team attach set --passphrase "something worth having"
loadout team say 20260918-1436-ed59 --node implementer/1 --message "stop, wrong file"
```

The page asks for that passphrase the first time and holds the grant in a
variable for as long as the daemon says — not in storage, because it is for a
person at a page and should go when the page does. The box only appears at all
when the node is still running and its own output is what you are looking at.

### What a run actually delivered

A node reports what it produced by reference - a commit hash, a branch - because
a hash is what it can say truthfully about work it has committed. It is not what
somebody asking "what did this run deliver" wants to read, and resolving it by
hand means knowing which repository the run used and typing git at it.

`loadout team outbox` does that for you: it takes the commits the nodes reported
and prints the files inside them, grouped by commit, with the node that
delivered each.

```
loadout team outbox                      # the most recent run
loadout team outbox 20260918-1436-ed59
```

```
20260918-1436-ed59  D:\gitilauncher

  78b32d6  implementer/1
    changed  README.md

  not committed
    plan     D:\gitilauncher\PLAN.md  3.7 KB  planner
```

Not everything a run makes gets committed, and the second block is why that
matters. On the run above the planner wrote `PLAN.md` into the repository and
reported it; an outbox built out of commits alone said that run had changed one
README and nothing else. A deliverable whose reference looks like a path is
therefore looked for on disk and reported with its size, or as **gone** if
nothing is there now.

Whether a reference is a path is settled by its shape, not by the kind the node
gave it: a plan is `PLAN.md` on one run and `round-3` on the next. A reference
with no whitespace that either carries a directory separator or ends in an
extension is treated as a path. Everything else stays a reference, and
`team status` reads it there.

Two limits, both deliberate.

A commit the repository no longer has is **named rather than dropped**. A run
that produced nothing and a run whose work nobody can reach look identical in a
list that simply leaves the second one out, and that difference is the whole
question.

Which repository a run used is not recorded directly, and is recovered: a node
given its own worktree is launched in that worktree, and a node without one is
launched in the repository itself, so the first node launched without a worktree
says where the run was working. That path outlives the worktrees, which are
cleared away after a merge. A run where every node had a worktree cannot be
resolved, and says so instead of printing an empty list.

Only commits are resolved against git. A decision is not a thing with files in
it, and asking git for one would report every run that made a decision as having
lost it - those still show in `team status`, under *delivered*.

### Two dashboards

The dashboard was built plain from its first commit, because the semantics had
to be right before anything was laid over them: a live region present at the
first paint, every state a word and not only a colour, real headings and lists,
40px targets, focus outlines that survive a forced-colours mode.

It then stayed plain for everybody, and that is a different decision — one
nobody actually made. Somebody who has said nothing about how they read a
screen was being handed the presentation designed for the hardest case.

So there are two, and which one you get comes from the accessibility profile
you already keep in `config.yaml`:

- **plain** under the `screen-reader` and `low-vision` presets, and under
  `accessibility.display.colour: none`. Those are the three settings that say
  something about reading a screen.
- **rich** otherwise, including under `colour-blind`, `dyslexia`, `adhd` and
  `plain-language`. None of those four is helped by taking the chrome away and
  two of them are about prose rather than pictures. What they *do* carry — a
  colour-safe palette, less motion — is honoured inside the rich page instead,
  because those say how a thing is drawn and not whether to draw it.

```
loadout team dashboard --view rich     # for this run only
loadout team dashboard --view plain
```

The flag wins over the profile, because somebody typing one means it now. It
does **not** override motion: asking to see the rich page is asking about its
chrome, not asking for your own motion setting to be overruled, and a page that
moved because a flag was typed would be the one thing that setting exists to
stop. `prefers-reduced-motion` is asked separately and the page takes the
quieter of the two answers.

Which page it is, and how much it may move, are written onto the opening tag by
the server before anything is drawn. A page that asked afterwards would draw
itself one way and then redecorate — a flash of the wrong thing for everybody,
and a redraw for the people most likely to have asked for none.

**The rich page is not the plain one with paint on it.** It has its own
structure: a band of live totals across the top, then runs grouped by what they
want from you — *Needs you*, then *Running*, then *Finished*, each under its own
heading with a count on it — as a grid of cards rather than a stack of
full-width rows. A card carries what a decision needs (what it is, what state,
how far in, what it cost, and who is in the room as a row of seats) and the rest
is one press away in the detail. Twelve cards fit on a screen; twelve rows are a
page of scrolling in which the one that needs you is as likely to be below the
fold as above it.

That means two renderers over one set of facts, which is a real cost and is the
one that was asked for. Neither computes anything: both read the same run object
the server sends.

The floor is the same on both, and it is why the cards say what they say. Every
card states its run's state **as a word**. Nothing on a card is carried by
colour alone — which is why the people in a room are drawn as *who is there* and
never as how they are getting on: a coloured ring would be a claim in colour
only, and the words for it live in the detail, where there is room to say them.
Every drawing — the round strip, the spend bar, the cost bars — is
`aria-hidden`, because every number in them is written out in words on the same
card, and announcing it twice is reading the card twice.

### Names, and things you can follow

Every name a person needs was already being worked out and sent; the page was
drawing none of it. A run heads its card with its room — *The Corner Office
(Plant Died)* — rather than `iterating-project - 20260917-1116-ed59`, a node is
the person it is rather than `implementer/1`, and `role.project-lead` reads as
*Project lead*.

The identifier is never taken away. It is what every command takes and what you
are usually about to type, so it stays on the card, quiet, monospaced and
selectable — it stops being the label and becomes the reference beside it.

Values you would go and look up are now pressable. A run's name opens it. A
node's handle, or its seat in the room, opens the run *and* selects that node's
own stream — which previously meant reading the handle, remembering it, opening
the run, and finding it again in a list of buttons.

### The stationery of an office that does not exist

The rich page is deliberately not the house style of every other agent
dashboard - the dark slab, the blue accent, the soup of rounded pills. Loadout
already has a language: a run is a room, a node is somebody at a desk, and the
rooms are called things like The Broom Cupboard (Keycard Only). So the page is
the stationery that office would use.

Manila and paper. An index card per run, with a ruled line under its name. The
state as a rubber stamp - uppercase, letterspaced, in a double rule, a degree
and a half off square. A desk plate per node with the initials on it. The
totals across the top as one ruled ledger rather than four cards. Dividers down
the side of the drawer for the destinations, and the one you are in reaches
across the rule onto the sheet it opens. Serif for anything read, typewriter
for anything you would type - an identifier, a figure, a label.

Nothing is fetched: no webfont, no stylesheet, no image. The paper grain is two
gradients at two per cent. A dashboard that pulled a typeface from the internet
would be a dashboard that does not work on the machine it is most wanted on,
which is one with no network.

The same tokens carry every screen, not only the list: changing view used to go
from a designed page to the one underneath it.

### Themes

Four, each with a light and a dark, chosen on the settings page and remembered
in this browser:

| Theme | What it is |
| --- | --- |
| **Paper** | Manila and ink. The default. |
| **Slate** | Cooler, and quieter about it. |
| **Oxblood** | Warm, with a red ledger to it. |
| **High contrast** | Black and white, and every rule drawn; nothing suggested by a shadow. |

Light or dark is its own choice - follow the system, or force one - because a
theme is not a mode. Forcing light on a dark machine used to give light
surfaces and the dark state colours together, and the figure saying how many
runs were going measured at 1.7:1; the state colours follow the chosen scheme
now rather than the machine's.

The accent is a set of six rather than a colour wheel: ink, oxblood, forest,
slate, plum, rust. Every one was measured against both of its theme's surfaces,
and a free picker cannot promise that. Density is comfortable or compact, and
moves the spacing scale rather than the type size.

All of it lives in this browser and nowhere else. A theme is a thing about this
screen in this room; the settings that travel are in `config.yaml`, and nothing
on the settings page writes there.

### Switching page, and being told

A **Plain view** button sits in the letterhead on both pages, in every view,
early in the tab order and never behind a menu - the page somebody is reading
is the thing they most need to be able to change about it. Pressing it writes
the choice down and asks for the page again rather than swapping it in place:
the presentation is settled before the first paint precisely so that nothing
draws itself one way and corrects itself, and honouring that means re-entering
the page rather than mutating it half-way. The settings page has the same
choice in full, with **As my profile says** as the default.

The change is announced into the live region the page has carried since its
first commit, so a screen reader that is running reads it.

**A web page cannot detect a screen reader.** There is no API for it; every
heuristic that claims to is wrong often; and the ones that work at all work by
fingerprinting somebody because of a disability. So the page never guesses —
**it asks the machine serving it**, which can tell, because Loadout already has
a channel to a running reader and already reports whether it was a reader or
the system's own voice that answered.

When one did, the page's announcements are said through it as well as written
into the live region. The live region is what works for a reader watching the
tab; speaking reaches one that is not, which is what a dashboard left open on a
second screen actually is.

Four conditions, all of them required, before this machine says a word:

1. **A screen reader answered.** Not a system voice — this speaks to the reader
   somebody is already listening to, and a page that started talking through
   the speakers because it found a voice installed would be a surprise, not a
   feature.
2. **You asked.** `loadout config set show-speech screen-reader`, the same
   setting the launcher uses, because somebody who has said "speak to me" has
   said it once and should not have to say it again per surface. Off by
   default, and off even under the `screen-reader` preset.
3. **The browser is on that machine.** Checked per request rather than per
   listener: a dashboard on `0.0.0.0` is reachable from the network, and a
   browser elsewhere making this machine talk is not something anybody asked
   for.
4. **The token,** like everything else here.

Nothing is ever said out loud that is not also written on the page, and a line
longer than 400 characters is cut — these are announcements, and a reader handed
a megabyte would be reading it for an hour. The settings page says which of the
four conditions is not met, in a sentence, rather than being quietly silent.

**Not verified:** there is no screen reader on the machine this was written on,
so the path where one answers has only been exercised against a stub. What that
stub does prove is the order of the refusals, which is the part that decides
whether somebody who never asked ends up with a talking computer. The real NVDA
route has still never answered here.

Movement can be turned down on the settings page and never up. Your machine is
asked separately through `prefers-reduced-motion`, and the page takes the
quieter of the two answers.

### What was and was not checked

The rich page was checked in a browser against the twelve runs on this machine:
heading order with no skipped levels, no list holding anything but list items,
every card stating its state in words, no control under 24px in either
dimension, no button without an accessible name, every drawing `aria-hidden`,
and text contrast in both colour schemes — 6.8:1 at worst in light, 9:1 at worst
in dark, against the 4.5:1 that is required.

One real defect came out of that pass and is fixed: the seats in a room were
identified by their edge alone at 1.2:1, well under the 3:1 a control's boundary
needs.

**axe-core 4.10.2 finds no violations on either page**, in any of the fifteen
states audited on 20 September 2026 — against the page's own markup and the
answers a live dashboard actually gave, rather than against a stub. It found
two things on the full page and both are fixed: the wordmark sat outside every
landmark, and the skip link pointed at a heading the settings page puts away.
The [accessibility statement](accessibility.md) has the detail, including the
two things axe declines to judge and why.

Still not done: **no screen reader has been near any of it**, and no
keyboard-only pass by a person has been recorded.

### Six screens, a board to put them on, and somewhere to set things

**List**, **Office**, **Graph**, **Timeline**, **Waiting** and **Terminal**
across the top of the runs pane. The first four show the same state and differ
only in how you look at it; the fifth shows what has not become a run yet, and
the sixth shows what one is doing right now.

- **List** — dense and complete. The working view, and the default.
- **Office** — one room per run, a desk per node. The glanceable one, the thing
  you leave on a spare screen. A finished room stays and empties: the agents
  leave, the name and the result remain, and you can walk back into it.
- **Graph** — who asked whom, as the delegation tree. This is what a list
  cannot show — the *shape* of a run — and it is the one to reach for when
  something is stuck. Every box is focusable in tree order and opens the run.
- **Timeline** — where the minutes and the money went, across the machine
  rather than inside one run. A strip per day with that day's totals, each
  scaled to the hours the day actually used.
- **Waiting** — what is queued rather than going: schedules that have not
  fired, and tasks nobody has finished. The one to look at before you go to
  bed.
- **Terminal** — what a node is doing, line by line, as it does it. It picks
  the newest running run's most recently active node rather than asking you to,
  because a screen you leave on should not need operating. It follows the tail
  only while you are already at the tail, so scrolling back to read something
  is not yanked away a second later.

The timeline collapses the empty days on purpose. One scale across everything
was tried first and is useless: runs span days and each lasts minutes, so every
bar came out at the minimum width and they all piled against the left edge — a
correct chart that said nothing. Real time is kept *inside* a day, which is
where overlap lives and the only place it matters: two teams running at once
are two teams running at once on an afternoon, never across a week.

**None of them can do anything.** Every control lives in the detail pane, so
answering a gate is implemented once rather than three times. Clicking anybody
anywhere takes you there.

#### Several at once, or one on its own

**Board** shows several screens together, and **a team's office is one of the
things you can add**. Tick `docs-crew` and you get that team's room as a panel
of its own; tick four teams and you get four offices side by side.

The viewer works out its own layout: one panel fills the window, four make a
two by two, nine a three by three, and it stops at six across because a seventh
column is a row of postage stamps. **Big** gives one panel the whole width.

Each team works in its **own office**, so four panels are four different rooms
rather than the same picture four times. Five rooms are fitted - an open-plan
office, a network operations floor, a newsroom, a trading floor and a corporate
headquarters - and a team is only ever put in one that has been. Which office
is worked out from the team's name — so a team is always in the same room and
you learn it — and the
picker on the panel changes it when the worked-out one is not the one you
wanted. Both that and which screens you chose are remembered in that browser
and nowhere else; neither reaches the daemon.

A panel does not copy a screen, it borrows it, so there is one office and one
terminal however you arrange them.

For a second monitor, any screen is also an address of its own:

```
http://127.0.0.1:8321/screen/office?token=<your token>
http://127.0.0.1:8321/screen/terminal?token=<your token>
```

The page is then the screen: no heading, no buttons, nothing to press by
accident while walking past it. It is the same dashboard and needs the same
token — putting it on another monitor is not a reason for any page in any tab
to be able to read it. An address naming a screen that does not exist shows the
whole dashboard rather than an error.

#### The waiting area

The other four views read the runs. This one reads the two things that have not
become runs: your **schedules** and your **tasks**.

Both, because they are not the same kind of thing and showing one without the
other answers half the question. A schedule is the machine's own intention — it
fires whether or not you remember it. A task is yours, recorded and dated, and
*nothing* will ever fire it. "Is anything going to start without me, and is
anything sitting here I said I would do" is one question with two answers.

Soonest first, because what the view is for is what happens next. Anything a
clock does not decide — a schedule watching for a commit, any task — comes after
everything a clock does; saying "due at" about those would be a guess dressed as
a fact. Anything **held** comes last and says what is holding it: a paused
schedule, a blocked task. Held is not a colour — the row says it in words and
the dashed border is the second encoding.

Only `open` and `blocked` tasks are here. A task somebody is `doing` is in the
office, not the waiting area, and `done` and `dropped` are not waiting at all.
At most twelve tasks are read from any one project, because a waiting area is a
glance rather than a backlog tool: one project with four hundred open tasks
would otherwise bury every schedule on the machine underneath it.

It reads and nothing else. Nothing here can fire a schedule or close a task.

#### Putting art in the office

Out of the box a desk is a square with the node's name and state written in it,
and that is the whole design: the words come first and a picture is a second
encoding on top of them, never the only one. Nothing below changes what a
screen reader is handed.

**Loadout ships no art.** Pixel-art asset packs are generally sold under
licences that let you use the files inside a finished project and forbid
redistributing the originals — and a public source repository hands everything
in it to anybody who clones. So the art lives on your machine and Loadout only
draws it.

A set is a directory; a piece is a file in it, named after the role it draws:

```
<state>/teams/office/open-office/lead.png
<state>/teams/office/open-office/implementer.png
<state>/teams/office/open-office/reviewer.png
<state>/teams/office/open-office/worker.png
```

```sh
loadout config set team-office-set "open-office"
```

A set can also carry the room itself, and where its desks are:

```
<state>/teams/office/open-office/room.png
<state>/teams/office/open-office/room.json
<state>/teams/office/open-office/front.png
```

`room.png` is the office **with nobody in it** — most packs ship an empty or
environment-only variant beside the populated one, and the empty one is the
one to use. The people in the room should be your nodes, not the artist's.

`front.png` is the **furniture that goes in front of the people**: the chairs
they are sitting in, and whatever else stands between them and the viewer. It
is optional, and a set without one draws as it always did. A set with one looks
like the artist's own scene instead, because a room drawn as a single picture
can only ever be behind - so a seated figure sat on top of the chair it was in,
legs across the seat and shoes over the castors, which is somebody standing in
front of their own chair rather than sitting in it.

The thing to understand about making one is that **it has to be the furniture's
own shape**. The obvious construction - copy a rectangle of `room.png` around
each desk and lay it back on top - does not layer anything. A rectangle has a
straight top edge, so the person stops at a horizontal line with a chair
somewhere underneath, and at the amount needed to hide the legs it leaves half
a person. Where the person is wider than the chair it is worse still: plain
floor from inside the rectangle erases them.

**Look in the pack first - but for a shape, not for pixels.** These packs ship
their furniture as individual transparent PNGs, and the trading floor ships
four facings of its swivel chair. Use that art as a **stencil**: scale it to
the height the chairs are drawn at in that room, stand it on its castors at
each desk, and copy `room.png` through its alpha. The outline is the artist's -
the curve of the back, the arms, the gap between them, a shoe showing past the
base - and every pixel is the room's own.

Pasting the art itself is the mistake to avoid, and it is an easy one to make:
the room already has chairs in it, so what you get is a second chair on top of
the first, a shade out and a pixel or two out of register. Spread the stencil
by a pixel or two as well. Overhang costs nothing, because it can only redraw
the room over itself; falling short leaves a sliver of somebody's leg across
the arm of their chair.

There is a test for having got this right, and it is worth running: laying
`front.png` back over `room.png` must give `room.png` **exactly**. Anything
that differs came from somewhere other than the room and will show up as a
ghost behind an empty desk.

The art cannot be *found* by matching - the best placement of one in its own
empty scene agrees on 43%, which says these are separate renders rather than
parts cut from the drawing. Each has to be placed and its height measured.
Check the facing too: the open-plan office and the headquarters ship one facing
each and it is not the one their rooms show, so the office's chair swallows a
person seen from the side and the headquarters' puts a seat cushion on the
sitter's back like a rucksack.

**It is not only the chair.** Anything standing between somebody and the
viewer belongs in front of them - the pedestal their legs go behind, the
planter at the end of the run, the partition, the bin. The rule for which is
the one every 2D engine uses and calls **y-sorting**: whatever stands nearer
the viewer goes on top, and on a top-down room "nearer" means "its base is
further down the picture". So for a person whose feet are at y, an object whose
own base is below y is in front of them, and one whose base is above y - the
monitor, the desk's far edge, the wall - stays behind, which is why a head can
overlap a screen.

Find those objects the same way. Around each desk, flood a generous box inwards
from its edges, following pixels that match their neighbour within a tolerance:
what the flood reaches is floor and the surfaces running out of the box, and
what it cannot reach is the things standing in it. Label those, and keep the
ones whose base falls below the person's feet.

**A base nearer than somebody is not sufficient on its own**, and getting this
wrong buries everybody. A desk is one connected mass that runs from behind the
person to in front of them - its near edge is nearer, its surface is further -
so sorting it as a single object puts the whole desk, monitors and all, on top
of whoever is sitting at it. What is genuinely in front of a seated person is
furniture no taller than they are. Anything rising past their shoulders is the
desk they are sitting *at*, or the partition behind it, and belongs behind
them. This is the limit of y-sorting that every engine shares: it orders whole
objects, and it cannot put an object's near half in front of somebody and its
far half behind.

**A thing only goes in front if it is in front of everybody it touches.** One
static layer cannot be in front of one person and behind another, and some of
these rooms have desk rows closer together than a person is tall. A chair at
the near row is genuinely in front of whoever sits in it and genuinely behind
whoever sits at the row above; kept on the nearer-base test alone, it covered
that second person completely. Check each candidate against every person it
overlaps and drop it if it should be behind any of them - but not against the
person sitting in it, because a chair's castors can fall a little above its own
desk's coordinate, and comparing a chair with its own occupant throws every
chair in the room away.

**And a desk where nobody can be seen is not a desk.** After the layer is
built, measure how much of the person at each desk it covers. Half of somebody
hidden is a person sitting at a desk; nearly all of them is an agent that has
simply disappeared from the room, which is worse than the legs-across-the-seat
this layer exists to fix. Drop those desks and build the layer again, because
the layer is built from where people are. Three newsroom desks went that way:
its two front rows are five and a half percent apart and its people are nearly
eight, so the near row's chairs covered the far row's people entirely.

That is also why the chair still needs its own stencil in two of these rooms.
The chair touches the desk in the picture, so the two flood as one object whose
crown belongs to the desk, and the rule above drops it. The front layer is the
union of both passes: the y-sorted objects, and the chair cut out by its own
outline. Both take their pixels from `room.png`, so the union is still exactly
the room and the test above still holds.

One line still has to be measured: where the furniture *starts* being in front,
because above it the person is in front of the desk and their arms belong on
top of it. It is **the top of the chair's own back** in the empty room, read
off a grid at half a percent a line, then turned into a share of the figure
standing there. It runs from about half in the trading floor and the network
floor to about three quarters in the open-plan office, whose chairs are seen
from the side rather than from above.

Guessing that line instead of measuring it fails in both directions. Too low
and a strip of the person runs down the middle of the chair. Too high and their
legs go behind furniture that is not there. The second one shipped here,
because that room's desks were a chair and a half to the right of its chairs
and nothing in the numbers said so. Look at one desk of every room, scaled up,
before believing any of it.

`room.json` says how big the scene is and where somebody stands in it:

```json
{
  "width": 1024,
  "height": 1024,
  "person": 8,
  "desks": [[20, 20], [15, 49], [30, 46], [43, 46], [15, 63]]
}
```

Each desk is a percentage across and down the scene, so the room draws
correctly at any width, and `person` is how tall a person is in that room -
also a percentage of the scene. Per room, because the packs do not draw to one
scale: a person is a twelfth of the open-plan office and a fifteenth of the
trading floor, and one size applied to all of them is right in one room and
floating over the furniture in the rest.

It is the whole figure, shoes included, and the desk's coordinate is where the
shoes land. Measure both from the artist's own person rather than from the
blob they make in a diff, which is them and their chair together: that mistake
drew everybody half again too tall, standing at the castors.

**How to get those numbers right.** Draw the room yourself and compare it with
the artist's. Composite one of the pack's own sprites onto the empty scene, put
that beside the populated scene, and slide it until the two agree: where it
agrees best is where somebody sits, and where no placement beats the empty room
there is nobody there. That last part is the test for whether a place is a desk
at all, and it is the only method here that has survived being checked.

Three that did not, each of which shipped its mistake:

**Diffing the populated scene against the empty one.** The obvious method, and
it was wrong in both directions. What it caught was not only people: a potted
plant, a filing cabinet and a wall of monitors whose animation differs between
the two renders all came back as somebody at a desk. And where it did find a
person it found the chair they had pulled out with them, because the empty
scene tucks its chairs in - so everybody was measured half again as tall as
they are and anchored at the castors rather than at their shoes. That is what
put people in front of their seats instead of on them.

**Finding the chairs by their own colour.** It found the meeting room's and
missed the trading desks' entirely at full size, then found the trading desks'
and missed the meeting room's once the image had been resized. A mask that
changes its answer when the picture is resampled is not measuring the room.

**Matching the pack's character PNGs into the scene.** Reasonable-sounding and
hopeless: those files are redraws rather than the scene's own pixels. The
correct placement of one agrees with the scene on 8% of its pixels, which is
indistinguishable from the wrong ones.

Two more things no method will tell you, and both were wrong until they were
looked for:

**Which way the chairs face.** A character sheet is usually front, side and
back, and the right one is whichever matches the desks. A newsroom's banks put
the monitor away from the viewer, so somebody at one is seen from behind; an
open-plan desk with the monitor to the right wants the side view. Take the
front view and everybody sits with their back to their work.

**Whether the artist's person was sitting.** A found placement is where the
artist drew somebody, not necessarily where somebody sits: a trading floor's
reception had two people standing at the desk, and a seated sprite put there
crouches in the middle of the floor. Keep the seats; leave the standing.

**Where the desks are, as opposed to where the artist put somebody.** They may
have populated the meeting room and left the desk banks empty. Where a room has
an obvious bank of identical workstations and only some were drawn occupied,
fill the rest along the row and column that were measured - and check the
result against the scene rather than trusting the arithmetic. The **lead takes
the first desk** and the workers take the rest in order; anybody the office has
no furniture for stands in a row underneath rather than being left out. A set
with no `room.json` draws its people in a row, which is what every set did
before rooms existed.

A pack that ships no usable empty scene is measured by eye off a percentage
grid laid over its populated one, and the corporate headquarters is the one
that needed it: its two master scenes are separate renders that disagree over
half their pixels, so nothing there can be diffed or slid.

The room describes itself so that adding a set needs no change to Loadout, and
a `room.json` that will not parse falls back to the row rather than taking the
view out.

**What moves.** A node that is working breathes, gently; one that is blocked or
finished is still. That is the only movement, and it is tied to what the node
is actually doing rather than being decoration — the packs' own animated scenes
are loops of *their* people, which are not the ones in your run. It stops
entirely under `prefers-reduced-motion`.

Rooms are laid out across the page rather than down it, so an afternoon's runs
fit on one screen.

Each desk looks for its own role — `implementer`, `reviewer`, `verifier`,
whatever the team calls them, minus the `role.` — and falls back to `worker`.
A role with neither keeps its empty square, so a half-finished set is a
partly-drawn office rather than a broken one. `png`, `webp` and `gif` are
served and nothing else is; a name that is a path reaches nothing.

Several sets can sit side by side and the setting picks one, which is the point
of a set rather than a folder: an office themed one way on Monday and another
on Friday is one config change. A name no directory answers to draws squares —
the same as having no art, rather than something subtly broken.

The waiting area has a set of its own, because a reception of people waiting and
a floor of people working are different rooms:

```
<state>/teams/office/lobby/waiting-1.png
<state>/teams/office/lobby/waiting-2.png
```

```sh
loadout config set team-waiting-set "lobby"
```

Pieces there are named `waiting-1`, `waiting-2` and so on, and are dealt out by
each item's own identifier — the same schedule gets the same person on every
redraw, which matters more than the variety does. A set with none of those falls
back to a piece named after the kind (`schedule` or `task`), then to `worker`,
then to an empty square.

The images are served by the daemon from that directory, over the same loopback
address and behind the same token as everything else on the page. Nothing is
fetched from the internet, which was true when the page had no images and is
still true now.

Each run is also given a **room**, which is a name somebody might actually
remember: *The Corner Office (Plant Died)*, *The Mezzanine (Lift Out of
Order)*, *The Breakout Space (Double Booked)*. A run is called
`20260918-1436-ed59`, which is precise, sortable and impossible to hold in your
head — and a week later "the one in the haunted meeting room" is how anybody
refers to it. Worked out from the identifier rather than stored, so the same
run is the same room on every machine that reads its journal.

**Built shape first.** The office is elements rather than a canvas: every desk
carries its name, its role and its state as words, and the sprite is an empty
square waiting for art. When the art arrives it becomes a second encoding on
top of a first rather than the only one, which is the decision that keeps this
inside the accessibility bar rather than beside it.

Rename any of them from the detail pane, or from a terminal:

```sh
loadout team name 20260918-1436-ed59 --room "The one that ate the budget"
loadout team name 20260918-1436-ed59 --clear     # back to the worked-out name
```

The name goes in one file beside the journal rather than into the journal
itself. The journal is a record of what happened; what somebody decided to call
it afterwards is not that, and putting it there would mean renaming a run by
appending to its history. Emptying the box on the page is the same as
`--clear`.

**Not built yet.** The art for the office and the movement that goes with it.

### Four depths of one run

Open a run and the pane on the right has four tabs, reached with <kbd>1</kbd>
to <kbd>4</kbd> from anywhere on the page, or with the arrow keys once you are
on the strip.

- **What it said** — at two resolutions. The run's own journal is one line
  every few seconds with repeats collapsed to a count, which is what watching
  wants; pick a node instead and you get everything that node did, every tool
  it called and everything it said, which is what working out where it went
  wrong wants. Sub-agents inside a node are marked as such.
- **What it cost** — what it is costing rather than only what it has cost,
  then every exchange one by one: which round, which node, which model, how
  many messages, what was refused, what it cost, and what the report came back
  as. Two nodes that cost the same are the same number and can be quite
  different problems; the totals cannot tell them apart.
- **The papers** — what each node was *told* to do and what it said it did, in
  the words the model actually saw. The run's own summing-up first, then each
  node's brief beside its report. This is where to look when a node did
  something reasonable for a brief nobody meant to give it.
- **What it changed** — the patch each node produced, file summary first. A
  report says "added the flag and a test for it"; the patch says what was
  added, and those are not always the same thing.

Two things about the papers and the patch. Names are matched against the shape
a run writes before they are ever joined to a path, so a name from a browser
cannot climb out of the run's own directory. And everything is redacted on the
way out — if it cannot be checked in a reasonable time, it is not shown at all,
rather than shown unchecked.

Each node's own stream is written as the run happens, in the launcher's
vocabulary rather than the agent's — so nothing reads an agent's JSON, a second
agent needs no change here, and a node's stream never carries whatever its agent
felt like printing. Anything one step said is cut to a readable length: a node
that reads a large file and quotes it back would otherwise put the whole file in
there several times over, and the file is on disk already.

Beside each node's patch is **Open a pull request**, which is the thing
anybody does next after reading one:

```sh
loadout team pr 20260918-1436-ed59 --node implementer/1 --draft
```

It pushes the branch and opens the pull request with `gh`, using whatever
GitHub login you already have. The body carries what a reviewer would otherwise
have to ask for — what the run was for, which node did it, what the checker
made of its report, what it cost, and the node's own words. The title is the
run's goal unless you give one.

This is the one button on the page that reaches a remote, so it asks first and
names the branch, and `--dry-run` says what it would push and open without
doing either. `gh` is checked for and checked as signed in *before* the push,
because an installed but unauthenticated `gh` offers a route that fails after
the branch has already gone.

A patch is measured from the commit the node's branch started at, which the run
writes down when it makes the worktree. Not from wherever the repository is now:
once a node's branch has been merged and tidied away, a diff against the current
state is empty, and empty reads as "it did nothing" rather than as "this cannot
be shown". Runs recorded before this was written down say so plainly instead of
guessing — a diff measured from the wrong place is worse than no diff, because
it looks like one.

### What needs you

A run that wants you is in the **Needs you** rail at the top of the dashboard,
and the tab itself says how many — `(3) Loadout teams` — so a buried tab still
tells you. `team status` says the same things.

Four reasons put a run there, and **each one clears itself**:

| Reason | Clears when |
| --- | --- |
| It has stopped and asked you something | you answer it |
| The lead took a round without asking for anything (another ends the run) | it asks for a node, or finishes |
| A node has said nothing for three times its own usual turn | it says anything at all |
| It has spent its budget, or is projected to overrun it | it slows, finishes, or you stop it |

Nothing is remembered and nothing is dismissed by hand: every reason is
recomputed on each read, so one that has gone is gone. A list that only grows
is worse than no list — it teaches you to skim the one thing meant to be
unskimmable.

Quiet is measured against each node's **own** average turn, not a fixed time,
because a reviewer reading one diff and an implementer running a suite have
nothing in common — with a two-minute floor, so a node whose turns take four
seconds is not reported after twelve. A node on its first turn is never called
quiet: nothing is known yet about how long that one takes.

### The numbers that change a decision

Four, above the exchanges, chosen because each one changes what somebody does
rather than decorating the page.

**What it is costing.** Not what it has cost — that is already on the list and
nobody acts on it — but the rate, and where that ends up if the run uses the
rounds it has left. A run projected past the budget its team set says so while
there is still something to be done about it, in words and with a border rather
than in colour alone. The rate is said per hour once it drops below a penny a
minute, because "$0.0041 a minute" is a number nobody has a feel for.

Projected from rounds rather than from the clock. A run does not spend evenly
through time — it spends while a node is up and nothing while the lead thinks —
but it does spend roughly per round, because a round is what buys nodes.

**Where the time went**, by round and then by node. The round narrows it down;
the node says which one to look at. Drawn as bars in the page itself: no
canvas, no library, and a screen reader reads the row, which says the name and
the number.

**What went wrong**, counted by shape: tool calls refused, reports the checker
would not accept, turns that had to be asked again, rounds that asked for
nothing, branches that would not merge. These are questions about the *team
file* rather than about the run. One run refusing twenty tool calls is a bad
afternoon; every run of one role refusing twenty is a role whose permissions
are written wrong, and the second only shows up once the first is counted.

All of it from what the run wrote down while it happened. Nothing is mined out
of an agent's own transcript files afterwards: those formats have changed
before, and a figure that quietly becomes wrong when somebody else ships a
release is worse than no figure. Runs recorded before a number existed do not
show it.

### How the roles have been doing

Under the runs, folded away, is what this machine has learned about its own
team file: one row per role and model, dearest first, with how many runs it has
been in, what it has cost in all and on average, how often its reports were
taken, and how many tool calls it is refused per run.

These are questions about the *team file* rather than about any run. One run
refusing twenty tool calls is a bad afternoon; every run of one role refusing
twenty is a role whose permissions are written wrong, and the second only shows
up once the first is counted across runs. The same for cost: what a reviewer
costs once is noise, and what it costs every time is a line somebody should
change.

Split by model as well as by role, because the question worth answering is not
"what does the reviewer cost" but "what does it cost on this model rather than
that one, and does it finish". Which model a node ran on has only been written
down since this was added, so older runs say so rather than being guessed at.

Read when you open it rather than on every refresh: it walks every journal on
the machine, and nobody needs that four times a minute behind a fold they have
not opened.

**Where each node's time went** sits under the same tab, from the gaps between
the things the node did: a gap ending in a tool call is the model deciding to
make it, one ending in that tool's answer is the tool running, one ending in
the node saying something is the model writing it. Gaps longer than two minutes
are counted as waiting rather than as any of those, because a node waiting on a
person would otherwise read as one that spent two hours thinking.

It is an account of **when things arrived** rather than of what a model was
doing, and the heading says "roughly" for that reason: deciding includes the
time an answer took to arrive, because nothing outside the model can tell those
apart. It answers "where did the twenty minutes go", which is the question, and
not "how long did it reason for", which nothing here can answer.

Only runs whose nodes kept a stream have it, which means runs from here on.

### Being told

The tab title says how many runs need you — `(3) Loadout teams` — and the
favicon changes with it. That costs nothing and needs no permission, so a tab
buried behind thirty others still tells you.

**Tell me when a run needs me** asks the browser for notification permission,
once, from a click rather than on load. Saying yes announces whatever is
already waiting, not just the next thing. **Sound** is a short chime,
synthesised rather than fetched, off by default; switching it on plays it once
so you know what you have agreed to. Both settings live in that browser only.

You are told once per reason. A reason that clears and comes back is news
again; three different questions on one run are three different tellings.

**Not verified.** The notification itself has not been seen to appear: the
browser automation used to test the rest of this cannot observe an operating
system notification, and could not stub the browser's own API convincingly
enough to prove the path. The counting, the title, the favicon and the
self-clearing all were.

### Told somewhere else

The browser tells you while you're looking at it, and unattended runs are
precisely the case where you aren't.

```sh
loadout team notify set slack --url "<your webhook address>"
loadout team notify test          # see it arrive before you need it
loadout team notify show          # whether, and where — never what the address is
loadout team notify clear
```

Slack, Discord, Teams, Telegram, or `generic` for your own endpoint, which gets
Loadout's own JSON. Telegram also needs `--chat`. Every message carries a link
back to the run, because a notice that says something is wrong and leaves you
to find it is half a notice.

The address lives in your operating system's credential store, never in a file
— a Slack or Discord webhook address *is* the credential: anyone holding it can
post into that channel as you.

**It only goes out while the daemon is running**, and it says each thing once.
A reason that clears and comes back is news again. A daemon that restarts
repeats whatever is still outstanding, once — what has been said is held in
memory rather than in a file nobody would ever read.

A notice the service refused does not count as said, and the next look tries it
again. The reason it was about is still true, so it would never have been new
again: one restarting chat service, or a minute without network, lost the
notice for the rest of the run and left it waiting for somebody who was never
told.

### Asking a Claude session what the teams are doing

The MCP server carries `loadout_teams`, so a session that is not part of a run
can answer "is anything of mine still going, and does it want me" without you
leaving the conversation to go and look. It gives what the dashboard and
`team status` give, from the same journals, so three accounts cannot disagree:
which runs are going, what each has cost and is costing, and what any of them
has stopped to ask.

**It reads and nothing else.** A session that could stop a run or answer a gate
could be talked into doing either by whatever it happened to be reading, and
the whole point of a gate is that a person decided. Stopping a run and
answering its question stay with the dashboard and the command line, and a test
asserts that no tool here is named for either.

### Running one again

A finished run has **Run it again**, which fills the start form from it — team,
goal, project, rounds and autonomy — rather than starting anything. A run that
has been run before is exactly the one somebody wants to change one thing about
before running again, and the earlier one stays where it is and stays readable.

**Not built:** re-running only the nodes that failed, or one node on its own.
Both mean re-entering a run that has ended rather than starting a fresh one,
which is a different and much larger thing.

### Changing a brief before it goes out

In **manual** mode every worker's brief is a checkpoint, and it is the one
question with a third answer. The lead wrote that task and the lead can be
wrong about it in a way that is obvious to whoever is watching and expensive to
find out any other way: the worker goes off and does the wrong thing,
competently, for ten minutes. Yes and no would make you choose between the
wrong brief and no brief.

On the dashboard the brief arrives in a box rather than behind two buttons —
change it and press yes, and that is what the worker is given. From a terminal:

```sh
loadout team gate --answer yes --instead "Add --since, and leave the tests alone"
```

Both halves go in the journal: what the lead wrote and what was sent instead. A
run where somebody quietly replaced a brief and the record only kept the
replacement is a record that cannot answer "why did it do that".

Supervised and autonomous runs are not offered this. A supervised run is
watched rather than driven, and offering every brief for changing would make it
manual mode under another name.

### Saying something to a node while it is still working

The lead reads messages between its rounds, which is the right place for
"change the plan". This is the other one: a worker has gone the wrong way and
is spending money doing it, and waiting for its turn to come back means waiting
for exactly the spend you are trying to stop.

Open a live node's own stream under *What it said* and there is a box for it,
or from a terminal:

```sh
loadout team say 20260918-1436-ed59 --node implementer/1 --message "stop, wrong file"
```

It reaches the node **the next time the node says anything** — between the
events the run is already reading, which is as often as it speaks and no more
often. A node that has gone quiet is a node nobody can steer, which is the same
thing the needs-you rail already says out loud. It is delivered once: a message
that arrived twice would be a node told twice, which reads as insistence rather
than as a bug.

#### It needs a second credential

**The dashboard's token does not grant this.** That token is for watching and
deciding — reading a run, answering a gate it *asked*, holding it, stopping it —
and every one of those is something the run offered to have decided. Typing at
a live node is not: it puts words into a process running with your file access,
at a moment nobody chose, over a port that may be on a network.

```sh
loadout team attach set --passphrase "something worth having"
loadout team attach show      # whether, never what
loadout team attach clear
```

The passphrase lives in your operating system's credential store with the other
credentials. The page asks for it once, exchanges it for a grant — thirty-two
random bytes the daemon made, not the passphrase and not derived from it — and
that grant stops working after thirty minutes whether or not anybody remembers
to give it back. A page left open on a laptop somebody walked away from stops
being one that can type at anything.

Nothing on this machine can attach until a passphrase is set, and the refusal
says so rather than reading as "you typed it wrong".

**What the agent does with it is the agent's business.** Claude Code's
stream-json input takes further user messages while a turn is running; another
agent may queue one until the turn ends. What Loadout promises is that it was
written to the agent and flushed, which is what its tests check — against a
pipe held open mid-turn, so the message genuinely goes in while the agent is
working rather than between turns.

### Reaching it from something other than this machine

The dashboard listens on `127.0.0.1` by default, which is this machine and
nothing else. `--listen 0.0.0.0` puts it on whatever network you are on, so a
phone on the sofa can answer a gate:

```sh
loadout team dashboard --listen 0.0.0.0
loadout team daemon --listen 0.0.0.0        # overrides teams.webhook_listen
```

It prints an address per way in rather than the wildcard, because `0.0.0.0` is
not something anybody can type into a phone, and it says plainly what has
changed: **anyone on that network who has the address can answer gates, stop
runs and start teams.** The token is the only thing in the way and it is part
of the address, so the address *is* the credential — treat it as one.

On Windows, binding anything other than loopback needs a reservation and fails
with "Access is denied" without one. The failure says which `netsh http add
urlacl` line to run rather than leaving somebody to conclude the feature does
not work. Loadout never runs it: adding a URL reservation changes the machine,
and that is yours to do.

### Starting one from the page

**Start a team** on the dashboard takes the same things `team run` does: a
team, what the run is for, a project, how many rounds, and an autonomy. It
asks once, naming the team and the goal, before anything starts — the token
got somebody to the page rather than to this, and this one spends money and
edits a repository.

Nothing on the page knows what a team is. Whether that team exists, whether
that project is registered and whether that autonomy is a word at all are
questions the command line already answers, and the page shows whatever it
says. The team and project boxes suggest names this machine has run before,
which is a convenience rather than a list to choose from.

It answers as soon as the run is under way rather than when it finishes — a
run takes twenty minutes on a good day and a browser holding a request open
that long has already given up. The run appears in the list within a few
seconds.

**Only the daemon's dashboard can start one.** `team dashboard` on its own
serves the page and nothing that runs commands, and says so plainly rather
than failing quietly.

### Answering, steering and stopping

A run that has stopped to ask you something is **waiting for you** — its own
state, not a kind of running, so a list of several teams shows at a glance
which ones need you.

```sh
loadout team gate            # what it is waiting on, and how to answer
loadout team gate --answer yes --reason "it needs the suite"
loadout team message --message "leave the tests alone"
loadout team halt --pause    # hold it before its next round
loadout team halt --resume
loadout team halt            # stop it after the round it is in
```

The same things are on the dashboard, as buttons, and they are the same
questions — answering in one place takes it off the other, because both write
the same file.

**Stopping is cooperative, and the wording matters.** A run checks between
rounds, so a stop lands when the turn it is in comes back. The alternative is
killing a headless agent mid-turn, which throws away the turn and what was paid
for it. Nothing here kills anything.

A message reaches the lead at the start of its next round, once. It is mid-turn
when you send it and cannot hear anything until it comes back.

A question is redacted before it is stored, so what reaches your screen, your
phone and the browser has had anything credential-shaped taken out of it. The
lead writes its own questions after spending ten minutes reading a repository,
and quoting what it found is how it asks about it. The options you choose
between are left exactly as written — they are matched against your answer, and
a changed one would be a question nobody could answer.

### Runs that ask a browser

A run started by hand answers at your terminal. A run with nobody at one — from
a schedule, a commit, or the webhook — sends its questions to the dashboard
instead, if a daemon is serving one. That is new capability, not just a
different screen: those runs used to have to decide everything themselves or
refuse.

Which one can answer is decided once, when the run starts, rather than per
question. Two places able to answer one question is a race whose loser leaves a
dead prompt on somebody's screen. Both `status` and `log`
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

**A run's own commits do not fire it again.** A run that merges a worker's
branch moves the checked-out branch, and the head is taken as seen once the run
has finished, so what the run did is not read as a reason to run. Without that
it is a loop: fire, merge, fire, merge, a team run a minute on an unattended
machine.

The price is that a commit somebody else makes *while* a run is going is taken
as seen too, so one trigger covers work that arrived during it. That is the
right way round — the alternative is a loop that cannot stop itself.

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
- The *people* at the desks do not animate. A set is one still per role, and a
  node working and a node waiting are told apart by the words on the desk, as
  they were before there was any art. The room behind them can move; they
  cannot.

## See also

- [The launcher](launcher.md) — watching a run from the terminal UI
- [Specialists and skills](specialists.md) — roles are specialists, and packs
- [First run and configuration](first-run.md) — the security profiles a team runs under
- [Commands](commands.md) — the whole command surface
