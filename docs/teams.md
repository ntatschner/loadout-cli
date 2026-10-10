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
lead. `--rounds` caps how many times it may come back, and by default nothing
does.

That default used to be five, and it was the wrong ceiling. A round is a crude
measure of something priced in money, and the team's budget is the cap that
actually binds: the run that changed this took four of its five rounds while
spending $20.84 of a $25 budget, so the limit in the way was never the limit
doing the work. What stops a run is the lead saying done, the budget, two rounds
without progress, or you.

A run cannot be uncapped in both, though. Two rounds without a request only
catches a lead asking for *nothing* — one that keeps asking for one more thing
trips neither that nor a budget its team does not set, and would spend until
somebody noticed. So a team with no `budget: usd:` and a run with no `--rounds`
is refused before it briefs anything, naming both. Every team that ships sets a
budget, so this is about one you wrote or edited.

You can run with no money cap, but you have to say so: `none` wherever a budget
goes (see below). A missing figure is somebody forgetting one; `none` is
somebody choosing it, and only the second gets past the refusal.

**Nodes that may run together do.** A node says how many instances may run at
once, and whether each gets its own git worktree. Briefing stays sequential,
because you cannot answer two gates at once, and so does starting, because two
`git worktree add` calls at once write one index.

**A branch that conflicts goes back to whoever wrote it**, under the same
instance name so it lands in the same worktree, told the branch, the target and
the files, and forbidden to touch the target or merge anywhere. It is retried
once and never again.

**A piece built on another starts on it.** A lead's request can say `from`: the
commit or branch a new worktree starts from, for a second implementer whose work
needs the first one's, not yet merged. Without it the tree starts at the
repository's head, whatever the task's words say. Given to a reviewer or
verifier, `from` names the work it reads instead: the node is started in
whichever of this run's worktrees stands at that commit or is that branch, and
where none does it runs in the repository and the run says so. A `from` that is
not the name of a commit or branch is refused and the lead told why.

**A reviewer or verifier is started where the work is.** When its brief names
exactly one of this run's branches, it is launched in that branch's worktree
and told so, and told not to commit or check anything out there. Naming two, or
none, leaves it in the repository — a node put in the wrong tree reviews the
wrong work and calls it fine — and a branch whose worktree has already gone is a
warning in the run.

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
there is no earlier moment to stop at. A lead picking up a long session can
spend a lot in one turn, so `team resume` tells you what its last turn cost
before it starts.

**No cap.** `usd: none` means the run has no money cap, on purpose. It is
different from leaving `usd` out, which means the team says nothing and the run
needs a budget or `--rounds` from somewhere else. A run with no cap still stops
on its wall clock, its turns per node, two rounds without progress, a lead that
says done, or you.

What a run is held to comes from the first of these that says anything:

1. the run itself — `team run --usd`, `team resume --usd`, `team budget`, the
   dashboard's budget box or its start form, or `team schedule add --usd` for
   the runs a schedule fires;
2. the team file's `budget: usd:`;
3. this machine's default, `loadout config set team-budget <figure or none>`,
   also in the dashboard's settings.

Each takes a figure in US dollars or `none`. Zero or a negative figure is
refused rather than read as no cap. Because the team file comes before the
machine's default and every team that ships sets a figure, `team-budget none`
reaches the teams you wrote without one — to take the cap off a shipped team,
say `none` for the run.

When a watched run reaches its budget it asks whether to raise it, and one of
the answers is to take the cap off. You can do the same while it runs with
`team budget <run> --usd none`.

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
- **No node can start a subagent.** Work a node wants done is asked for in its
  report, so that it gets a node, a branch, a review and a line in the journal.
  Claude Code starts a subagent without asking, so being left off a role's
  allow list does not stop one; `Agent` and `Task` are denied to every node
  whatever its role says. A lead in a trial run started one to write the code
  itself, and the run was over in a round.
- **A shell command is judged part by part.** `cd tree && dotnet test | tail -5`
  runs if every part is one the role allows, and is refused, naming the part, if
  any one is not — so an allowed first word no longer carries whatever follows
  it. Changing directory is never the part refused. Writing a file with `>`
  needs a role that may write files. What cannot be split safely — `$( )`,
  backticks, a here-document, an unclosed quote — is refused whole, with one
  exception: `git commit -m "$(cat <<'EOF' … EOF)"`, which is how Claude Code
  writes a commit message. With its delimiter quoted, that expands nothing and
  runs nothing but `cat`, so it is read as the text it is. Nodes that
  do not implement used to be left to the agent's own matcher, which reads a
  rule as the start of the whole line and refused every one of those examples:
  the first long team run's verifier was refused its first command three times
  over and verified nothing.
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

`loadout team new quick-review` writes that file for you and says where it is;
`loadout team edit quick-review` opens it. Neither does anything you could not
do with a text editor, which is the point — the file is the thing, and a command
that asked you twenty questions and assembled one would be a second way of
describing a team that has to be kept in step with the parser that reads it.

Copying is nearly always the better start:

```
loadout team new docs-crew-mine --from docs-crew
```

That copies the file rather than regenerating it, so the comments, the key order
and the inline maps survive. A template — `product-company` is the one that
ships — exists only to be copied, and the copy is not a template, so you can run
it.

A team you write is available to every project. `--for-this-project` puts it
under one instead.

`loadout team remove <team>` moves one you wrote to the bin, and
`loadout team restore <team>` puts it back where it was (see
[The bin](#the-bin)). The ones that ship and the ones from a pack are refused,
and the refusal names the copy that gets you past it: editing a file inside a
pack checkout is what the next `pack update` overwrites.

A team of yours lives in the workspace, so its going is a change there like any
other, and the command says so. Until `loadout workspace save`, the file's
removal is on this machine only.

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

## Saying when it is done

A run takes a goal. Set nothing more and it is held to **the team's own
done-when**, if the team file gives one — see [A team's own](#a-teams-own) below.
If it gives none, **the lead proposes what done means, and you agree it** — see
[If you say nothing](#if-you-say-nothing). Or say it yourself, up front:

```sh
loadout team run docs-crew "make the docs true" \
  --done-when "every command in docs/commands.md exists" \
  --done-when "the suite passes on a clean checkout" \
  --done-when "the changelog names the change"
```

Repeated rather than one comma-separated string, because a criterion is a
sentence and sentences contain commas.

What that changes:

- **Every node is told them**, for the same reason every node is told the team's
  standing goal. A worker given a narrow job still needs to know what the run is
  being judged on.
- **The lead's brief says it owes a verdict on each**, and its final report
  carries one entry per criterion: `met`, `unmet` or `not-attempted`, and every
  `met` saying in `because` which node, which report and which evidence shows it.
- **It says how it read each one**, in `understood`, and how it read the goal, in
  `goal_understood`. A verdict is only worth the reading it was given: "the tests
  pass" read as "the new test passes" can be met while the suite is red, and
  without the reading beside the verdict nobody could see which was meant. Runs
  from before this have no readings, and show none.
- **A `done` that leaves one unmet or unanswered is sent back to the lead**, with
  a reason naming the criterion. This is not new machinery: it is the rule that
  already governs a worker's report — `done` needs evidence that passed —
  applied at the level of the goal.

`unmet` and `not-attempted` are separate on purpose. An unmet criterion was
tried and needs a different approach; one never attempted means a whole area of
the goal was missed, and that is the thing a run of several rounds loses
quietly.

### A team's own

A team file can say what its runs are judged on when whoever starts one says
nothing:

```yaml
name: bug-hunt
done_when:
  - "a test reproduces the bug: it fails without the fix and passes with it"
  - "the full test suite passes with the fix in place"
```

Quote each one. A criterion is a sentence, and a sentence with a colon in it is
something else to YAML — a team file that fails to read is not listed at all.

Three that ship carry one: `docs-crew` (every changed page passes `docs audit`,
and every changed claim was checked against the code), `bug-hunt` (the two
above), and `dependency-sweep` (every updated dependency passes the suite on its
own branch). Each is what the team's job means by done. `iterating-project` has
none on purpose: it is the general one, and "the suite passes" is not true of a
run whose goal was a design.

**Yours replace the team's; the two are never merged.** Somebody who writes
their own has said what done means for this run, and the team's added on top
would hold them to things they did not ask for. With the team's, the lead is not
asked to propose any — the team's author already answered that — and the
journal records `by: team`, so a run read back says whose criteria they were.
`team show` lists them, and the dashboard's form shows them in its empty box.

### The project's own

Some criteria are true of every run on a repository, whatever the run is for:
the suite passes, the docs are in the house voice. Saying those every time is
the sort of thing that gets forgotten on exactly the run where it mattered, so a
project can say them once, in its `project.yaml` in the workspace:

```yaml
teams:
  done_when:
    - text: "the suite passes"
      teams: [all]
    - text: "docs are in the house voice"
      teams: [docs-crew]
```

Each one names the teams it applies to, or `all`. One that names none applies to
every team, because an empty list is far likelier to mean "everywhere" than
"nowhere".

**These are held on top, not instead.** Unlike the team's own, they do not step
aside when a run brings its own criteria: whatever the run, the team or the
lead's agreed proposal says, a matching run is held to these as well, and a
`done` that leaves one unanswered is sent back like any other. They also do not
count as an answer to what this run is for, so a run given nothing else still
has its lead propose criteria, and the box you agree them in says the project's
are held as well. They are generic hygiene; the proposal is what makes this
particular run's done mean something.

They live in the workspace rather than in the repository, because the manifest
is already committed and reviewed there and has one reader. A criterion only
ever makes a run check more, so it grants nothing anybody would need to approve.

To leave them out of one run, untick them on the dashboard's form, where they are
listed under the box for your own, or on the command line:

```sh
loadout team run docs-crew "tidy the guides" --no-project-done-when
loadout team run docs-crew "tidy the guides" --drop-project-done-when "docs are in the house voice"
```

Either way the run's journal records a `criteria.defaults` line naming what it
was held to and what was left out, and by whom, so a run read back can answer
"why was it held to that?" `team status` marks a project's criteria with
`(project)`. A run that nobody is watching, started by a schedule or a webhook,
has nobody to untick anything and is held to every one that matches.

A run picked up with `team resume` keeps the ones it started with, even if
`project.yaml` has changed since. They cannot yet be unticked at the point where
you agree the lead's proposal; that box shows them and holds them.

### How the lead reads them, before anything starts

A criterion is a sentence somebody wrote in a hurry. "The tests pass" read as
"the new test passes" can be met while the suite is red, and the coverage
entries above only show that reading at the end, beside the verdict, once the
work it was planned on has been paid for.

So once a run has criteria, every report the lead makes carries `readings`: each
criterion repeated exactly, and what it takes it to mean in a sentence or two. A
report without one for every criterion is sent back once, like any other
incomplete report. The journal records them the first time and again only when
one changes (`reading`, then `reading.changed`), so `team log` shows how the
lead's understanding moved.

The first time the lead asks for workers, a supervised or manual run stops and
puts the readings to you as an ordinary question, recommending *Accept these
readings*:

```
Before any worker starts: is this how you mean the done-when? The lead is
saying how it reads each one, not proposing new ones.
 • "the suite passes" read as: the whole suite, on a fresh clone
```

Accept, and the workers start. Choose **Think again** and nobody is briefed that
round: the lead is told its readings were sent back, restates them, and asks
again, and you are asked again. To say what you meant instead, use `team message`
before you answer.

Because it is an ordinary question, `--take-recommendation-after` times it like
any other, so a timed run still goes unattended: when the wait is up the
readings are accepted and the journal says nobody answered. An autonomous run,
or one with nobody at a terminal or a dashboard, writes the readings down and
carries on, and the journal says they stood with nobody to ask. A run picked up
with `team resume` is not asked again once its readings were accepted.

### What became of each one

When `team run` ends it prints one block per criterion, and `team status` and
the run's page on the dashboard show the same blocks:

```
  Done when 1 of 2 met
  + met           the suite passes  (project)
      taken to mean: the whole suite, on a fresh clone
      verifier/1's report: dotnet test, 4071 passed
      delivered: commit a4f21c9 by implementer/1
  - unmet         the docs say so
      taken to mean: the --since option is in docs/commands.md
```

The reading is the one the lead last stated, not the one it wrote beside its
verdict, so you see what it was working to. A criterion it read and never gave a
verdict on, because the run stopped first, is listed with no verdict rather than
left out.

"Delivered" is checked rather than taken on the lead's word. The lead lists in
each coverage entry the refs of what meets it, and only refs a worker's own
report handed back are shown as delivered. Anything it cited that no worker
reported is shown as *cited but no worker reported it*. That includes refs only
the lead's own report lists, since a lead can't count as delivering what it
judges.

### If you say nothing

A run with no criteria used to be held to one: *"the goal is met, with the
evidence cited from your nodes' reports"*. Read it again — it is a restatement,
not a check, and the lead writes its own verdict on it. Nothing a lead could
report was ever wrong.

That is not a theoretical hole. A run asked to *"refine my ones and make some
suggestions of your own"* dispatched a planner, a reviewer, a verifier and
another planner, never launched an implementer, produced one design document,
reported `done`, cited itself, and was right by the only rule it had. It cost
$20.84. A goal somebody wanted code from would have got exactly the same `done`.

So the lead is now asked. Its first brief — and only when you gave no
`--done-when` — tells it to propose criteria in `proposed_done_when`: each one a
thing somebody else could check, each specific to this goal, and not "the goal is
met". Before a single worker is briefed, what it proposed is put in front of you
in a box, the same way a brief is:

```
What this run will be judged on. The lead proposed these; change them if
they are not what you meant, one per line.

  loadout usage --since 2026-01-01 prints only entries after that date
  a test fails without the option and passes with it
```

Whatever comes back is what the run is held to, by the machinery above —
numbering stripped, because a list handed to somebody in a box comes back as a
list and `1. the tests pass` would never match a lead's coverage for `the tests
pass`. The lead is told the agreed list in its next round, since a lead held to
criteria it has not read cannot report coverage for them. Both the proposal and
what was agreed go in the journal, so a run where you changed them says so.

Empty the box and the run carries on with nothing checking it, exactly as it did
before. That is a real answer to the question and not a failure.

**An autonomous run has nobody to ask,** so the lead's proposal stands and the
journal records `by: nobody`. That is still worth more than what it had: a lead
held to specifics it wrote itself can report one of them `unmet`, which "the goal
is met" could never do.

`team status` then shows where each one got to:

```
  make the docs true
    taken to mean: every page under docs/ agrees with the code it describes

  Done when 2 of 3 met
  + met           every command in docs/commands.md exists
      taken to mean: each command listed there is one `loadout --help` knows
      docs-auditor/1 checked all 159
  + met           the suite passes
      taken to mean: the whole suite, not only the tests this run added
      verifier/1 reported 2843 passing
  ! not attempted the changelog mentions it
```

The dashboard's run form takes them one per line, and a run open in the detail
pane shows the same account.

**A run given no criteria behaves exactly as it did before**, which is what
keeps every team file and every script already written working. The trade is
plain: no criteria means "done" is the lead's word for it.

### What is not checked

- **Whether the criteria cover the goal.** They are your list. Nothing reads the
  goal and tells you a criterion is missing.
- **Whether the evidence is true.** `because` is a sentence the lead wrote. What
  is enforced is that a claim of `met` cites something, not that the something
  says what the lead says it says.
- **`stop_when` in a team file.** It is parsed, printed by `team show`, and read
  by nothing that runs. `goal_met`, `budget_spent` and `no_progress_2_rounds`
  are hard-coded in the loop whatever a team file lists.

A lead that will not account for the goal is asked twice — a returned report
goes back once, and the second answer is the node's whatever it says — and then
the run stops arguing. It does not record a done it cannot support: the run ends
saying how many criteria were left unmet, and the journal carries a `goal.unmet`
line naming them. An autonomous run nobody watched must not read afterwards as
having met a goal it did not.

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

**A node cannot turn this key.** Every node of a team run is started with
`LOADOUT_TEAM_NODE` set, and `team remedy trust`, `tools trust`,
`tools verify --agree`, answering with `team remedy requests` and `team gate`
all refuse when they see it. The roles' deny lists already covered some of
these, but a deny list matches the words of a command, and a permission you
grant from the dashboard can match more than you meant: a lead once granted
`loadout tools:*` to run a search had `loadout tools trust` with it. Listing
what is waiting still works from a node. It keeps an honest node honest; a role
that can run arbitrary script could clear the variable first, which is one more
reason the remediator is the only role with an interpreter.

### Who can run one

Exactly one role: **`role.remediator`**. Every other role in the library may run
git, `dotnet build` and `dotnet test`, and commands that only read — `ls`,
`cat`, `grep` and the like — and no interpreter, so it cannot execute a script at all — which
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

  **If a daemon is already serving, this command points at it** — it prints the
  daemon's address, opens it with `--open`, and starts nothing. A second server
  is a second port, a second token and a second set of buttons over one journal,
  and until 22 September 2026 that is what you got: somebody who had enabled the
  daemon and reached for the obvious command was quietly sent to a different
  page. `--port`, `--listen` and `--watch-only` are asking for a server of your
  own and still get one.
- **Tools → Team runs…** in the launcher — the same, in the terminal UI. See
  [The launcher](launcher.md).

`loadout team runs` lists what has run; `loadout team log` prints everything one
wrote down, and `--follow` keeps reading as it writes.

### Clearing out old runs

Every run keeps a directory of what it did — its journal, the briefs, the
reports and one stream per node, under a megabyte for a four-minute run. Disk is
not the reason to clear them; the listing is. `team runs` is the first place
anybody looks, and until now it showed every experiment anyone had ever started.

```
loadout team runs remove 20260917-1116-ed59
loadout team runs prune --keep 20 --older-than 30d
loadout team runs prune --failed
loadout team runs prune --outcome stopped --outcome limited --older-than 7d
```

`remove` takes the runs you name. `prune` takes them by the handful, and will
not run without being told which: `--keep <count>`, `--older-than <age>`,
`--failed`, `--outcome <ending>`, or several. Together they narrow each other —
*the failed ones older than that, but never below the newest count* — which is
what somebody typing them means and the most cautious reading available.

The endings you can ask for are `done`, `failed`, `stopped`, `limited` (out of
rounds or budget), `blocked`, `needs-decision`, and `unrecorded` for a run that
recorded a finish without saying how it went. A run files itself as it ends, so
rewording an ending later cannot re-file runs that have already finished, and
anything already on this machine is read from the sentence it wrote.

One thing that follows from being careful rather than clever: an ending nothing
recognises — one a later version writes, say — is never taken by an `--outcome`
or a `--failed`. Asking for the failed ones is asking for the ones known to
have failed, not for everything that could not be ruled out. `--dry-run` says
how each run it picked was filed.

Three things it will not take:

- **A run that has not finished.** Its directory is not only a record: a gate is
  answered by a file appearing in it, so deleting one under a live run leaves
  processes waiting on answers that can no longer arrive. `remove` refuses one
  unless you pass `--force`; `prune` never takes one at all.
- **A run that left a branch nothing merged.** The branch is still in Git and
  outlives the run, but the journal is the only thing on the machine that says
  which run produced it. `--include-unmerged` takes them anyway.
- **Anything at all, under `--dry-run`**, which lists what it would take and
  says why it is keeping the rest.

Nothing here touches Git. A run's branches and its working trees outlive it, and
a command called "forget the notes about it" that also deleted the work would be
the worst kind of surprise — so it names what it is leaving behind instead.

### The bin

`remove` and `prune` do not delete a run; they move it to the bin, and
`team remove` does the same with a team of yours. All three are typed while
clearing up, which is when the wrong identifier gets pasted, and a run's journal
is the only record of what it did. Before the bin, there was no way back from
either.

```
loadout team bin
loadout team runs restore 20260917-1116-ed59
loadout team restore quick-review
loadout team bin empty --older-than 7d
```

`team bin` lists what is there: whether each is a run or a team, when it was
removed, how many days it has left and how much disk it holds. `team runs
restore` puts a run back under its own identifier, and `team restore` puts a
team's file back where it was — under the project it was written for, if it was
written for one. A team removed more than once comes back as the copy removed
most recently; the older copies stay listed.

Neither will restore over something already there. A run with that identifier,
or a team with that name, is refused rather than replaced, because replacing it
would be losing the other one — which is the thing the bin is for.

Things stay in the bin for 30 days, then go for good.
`loadout config set team-bin-days <days>` changes that, and `0` keeps everything
until you empty the bin yourself: of the two ways to read a zero on a setting
that deletes things, deleting at once is the one nobody can take back. The
daemon clears out what has run out once an hour, and so does every removal, so a
machine that never runs the daemon still clears its bin — but only when
something new goes into it.

`team bin empty` deletes for good, and is the one command here with no way
back. It names everything it would take before asking, needs `--yes` where
nobody is at a terminal to ask, and under `--dry-run` lists what it would take
and deletes nothing. `--older-than <age>` takes only what has been there longer.

Two limits worth knowing. The bin holds what these commands put in it and
nothing else: a run's branches were never touched, so they are not in it either.
And a team's file in the bin is a copy on this machine; the workspace's history
is still the record another machine sees.

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
given its own worktree is launched in that worktree, a reviewer or verifier sent
to read one is launched in that one too, and any other node is launched in the
repository itself, so the first node launched in neither says where the run was
working. That path outlives the worktrees, which are
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

### Five screens, a board to put them on, and somewhere to set things

**List**, **Office**, **Graph**, **Timeline** and **Terminal** across the top of
the runs pane. The first four show the same state and differ only in how you
look at it, and the fifth shows what one is doing right now. What has not become
a run yet waits in the office's lobby.

- **List** — dense and complete. The working view, and the default.
- **Office** — the runs as a building, a floor per run and a desk per node. The
  glanceable one, the thing you leave on a spare screen. A finished floor goes
  dark for an hour and you can still walk into it. [The office](office.md) has
  the whole of it: the views, the lobby, the roof, the basements, and what the
  rooms can do.
- **Graph** — who asked whom, as the delegation tree. This is what a list
  cannot show — the *shape* of a run — and it is the one to reach for when
  something is stuck. Every box is focusable in tree order and opens the run.
- **Timeline** — where the minutes and the money went, across the machine
  rather than inside one run. A strip per day with that day's totals, each
  scaled to the hours the day actually used.
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

**The controls live in the detail pane**, so answering a gate is implemented
once rather than three times, and clicking anybody anywhere takes you there.
The office's rooms are the exception that proves it: a room's popup offers
what that room is for - stopping a schedule from the waiting room, emptying
the bin from the basement - and each one runs the same command.

**Ideas**, beside the settings, is not a view of the runs either: it's where
ideas are dropped in and worked through into plans, a button for each command.
[Using the dashboard](guides/dashboard.md#9-work-on-your-ideas) walks through it.

#### Several at once, or one on its own

**Board** shows several screens together.

The viewer works out its own layout: one panel fills the window, four make a
two by two, nine a three by three, and it stops at six across because a seventh
column is a row of postage stamps. **Big** gives one panel the whole width.
Which screens you chose is remembered in that browser and nowhere else; it
never reaches the daemon.

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

#### What is waiting

The views read the runs. The office's lobby also reads the two things that have
not become runs: your **schedules** and your **tasks**, a person on a sofa for
each.

Both, because they are not the same kind of thing and showing one without the
other answers half the question. A schedule is the machine's own intention — it
fires whether or not you remember it. A task is yours, recorded and dated, and
*nothing* will ever fire it. "Is anything going to start without me, and is
anything sitting here I said I would do" is one question with two answers.

Soonest first, because what the waiting room is for is what happens next. Anything a
clock does not decide — a schedule watching for a commit, any task — comes after
everything a clock does; saying "due at" about those would be a guess dressed as
a fact. Anything **held** comes last and says what is holding it: a paused
schedule, a blocked task. Held is not a colour — the row says it in words and
the dashed border is the second encoding.

Only `open` and `blocked` tasks are here. A task somebody is `doing` is on a
floor, not in the lobby, and `done` and `dropped` are not waiting at all. At
most twelve tasks are read from any one project, because a waiting room is a
glance rather than a backlog tool: one project with four hundred open tasks
would otherwise bury every schedule on the machine underneath it.

The waiting room's popup can stop a schedule, asking first. Nothing here can
fire a schedule or close a task: a task is somebody's record of work, not the
page's to delete.

#### Art for the office

Out of the box the building is drawn from the **Tech office**, the art Loadout
ships, made for it. It is unpacked into `<state>/teams/office/loadout-tech/`
when the dashboard or the daemon starts, and brought up to date when Loadout
is. Copy it to a name of your own to change it: the built-in folder is written
over on an update. Any other set is your own, under `<state>/teams/office/`,
and stays there. Asset packs are generally sold under licences that let you
use the files inside a finished project and forbid redistributing the
originals, so Loadout ships none of those, only draws them. A set that is a **kit** - floor and wall tiles, furniture,
people, the outside - is what the building is made from; [the office](office.md)
says what goes in one and how `loadout team office check` reads it.

```sh
loadout config set team-office-set "my-office"
```

A painted room from before the building, one picture with desks placed over it,
is a set too: its files stay where they are, and nothing draws them any more.

The images are served by the daemon from that directory, over the same loopback
address and behind the same token as everything else on the page. Nothing is
fetched from the internet.

Each run is also given a **room**, which is a name somebody might actually
remember: *The Corner Office (Plant Died)*, *The Mezzanine (Lift Out of
Order)*, *The Breakout Space (Double Booked)*. A run is called
`20260918-1436-ed59`, which is precise, sortable and impossible to hold in your
head — and a week later "the one in the haunted meeting room" is how anybody
refers to it. Worked out from the identifier rather than stored, so the same
run is the same room on every machine that reads its journal.

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

### Running and making a team from the page

Two forms, and the difference between them is the whole point of there being
two.

**Run a team** takes the same things `team run` does: a team, what the run is
for, a project, how many rounds, and an autonomy. The team and the project are
lists of what actually exists on this machine, read from the same catalogue
`team list` reads. Picking one shows what it is, how many nodes it has and
anything wrong with it, because eight names in a list tell you nothing about
which to pick. A template is offered as something to copy and not as something
to run, which is what it is.

The project is required here, though the command line lets it default. A
terminal defaults to the directory you are standing in, which is usually the
repository you meant. A dashboard has no such directory: the daemon's is
wherever it happened to be started.

It asks once, naming the team and the goal, before anything starts — the token
got somebody to the page rather than to this, and this one spends money and
edits a repository.

**Make a team…** and **Run one on a schedule…** each open a sheet of their own
rather than another fold under the runs list. They are native `<dialog>`s, so
the browser traps focus inside them, returns it to the button that opened them,
closes on Escape and marks them modal to a screen reader.

Making a team writes one and tells you where it is, and is the other half of the
same story as running one. Scheduling asks for a name, a team, a goal, a project
and one of three ways to say when — a time of day, an interval, or something to
watch for. Manual is not offered, because the command refuses it: manual means a
person at every step and nobody is watching at 23:00.

A schedule made here waits in the office's lobby, and the waiting room's popup
is where you stop one. **Nothing fires unless the daemon
is running**, which the sheet says rather than leaving you to find out at the
appointed time.

Everything that decides whether a run can begin — the team existing, the project
resolving, the tree being a repository — is settled in the first seconds, so the
page waits that long before saying a run has started, and shows the refusal when
there is one. It does not wait for the run itself: that takes twenty minutes on
a good day, and a browser holding a request open that long has already given
up.

**Forget it**, on a run that has ended, runs `team runs remove` against it. A
run still going does not offer it, and the command refuses one anyway.

**Forget**, beside the identifier on every finished run in the list, is the
same thing from where you are looking rather than from inside the run: it asks
by name and runs `team runs remove`. It is on the cards and on the plain rows,
through one control, and a run still going does not draw it.

**Clear out runs**, folded away above the runs list, is the same thing by the
handful: pick an ending, an age, how many of the newest to keep, and it runs
`team runs prune`. It says what it is about to do in a sentence and asks before
it goes. Nothing about which runs those are is decided in the browser — the
page types the command, and the command is what refuses a live run.

A watch-only dashboard draws none of this. `team dashboard --watch-only` serves
a page that cannot change anything, is told so, and puts the controls and both
forms away rather than leaving buttons that do nothing.

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

### When nobody answers: taking the lead's recommendation

Every question a lead asks comes with its recommendation. For a run nobody is
going to sit and watch, you can say how long a question waits before that
recommendation is taken for you:

```sh
loadout team run docs-crew "make the docs true" --take-recommendation-after 30m
```

or in the team file, under `rules`, as `take_recommendation_after: 30m`, or in
the dashboard's form. The run's own setting wins over the team's.

Write the time as `30m`, `2h` or `1d`, or with the unit spelt out - `30 mins`,
`1 hour`, `2 days`. A bare number is refused rather than guessed at, because ten
minutes and ten hours are different runs. `never` (or `off`) for a run means no
limit, even when the team sets one, and the dashboard's box treats it the same
as leaving it empty.

When it happens, it happens the way you would have answered: the answer is
written as the question's answer, so the question leaves the dashboard, the run
carries on, and the journal says which it was. It is never recorded as a
person's choice.

What that answer is depends on `on_timeout`, set the same three ways
(`--on-timeout`, `on_timeout:` under `rules`, or *When nobody answers* on the
dashboard's form):

- **`think-again-once`, the default.** The first time a question goes
  unanswered it goes back to the lead to think again, exactly as if you had
  chosen *Think again*, and the lead is told nobody chose anything. If it asks
  again and that times out too, its recommendation is taken. The lead has had a
  round more evidence by then, which is the point of asking it to look again.
- **`recommend`** takes the recommendation at once. This is what every timer did
  before `on_timeout` existed.
- **`think-again`** sends it back every time.

Each send-back is a round of its own, so the round limit and the budget still
stop a run, `think-again` included. The question about the lead's readings
before any worker starts is timed the same way and counted as one question,
even though its wording changes each time the lead restates them. The journal
says `sent back to the lead to think again, nobody having answered in 30m`, or
`took the lead's recommendation, nobody having answered in 30m`.

While it waits, the question says when the timer will act and what it will do,
and both the dashboard and the launcher's **Team runs** screen count down to
that: *12 minutes left, then it goes back to the lead to think again*. Without a
timer they count down to the run giving up instead. That used to be the only
countdown, and it read "then the run stops" over questions the timer was about
to answer. In the launcher, **a** answers the selected run's question with
`team gate`, the same command the dashboard runs.

What it does not do:

- **Only the lead's own questions.** Never a merge, never anything that leaves
  this machine: those carry no recommendation, and they are the ones a person
  has to say yes to. Never a brief in manual mode either, which exists so a
  person approves each thing the run spends money on.
- **Only a run answered from the dashboard** — one started from the page, the
  daemon or a schedule. A question at a terminal is a prompt that waits until you
  answer, and nothing here can answer it for you.
- **A resumed run takes the team's rule**, not a wait the run was first started
  with on the command line. The journal records that wait, but as a note, and
  quietly reapplying an old flag to a new start would be something you had not
  asked for this time.
- An answer and the wait running out in the same instant: whichever is written
  second wins. If yours arrives first, it is yours and is recorded as yours.

### Think again

A lead's question has one more answer than its options: **Think again.** It is for
when none of the options is right and saying which would be is the lead's job,
not yours — you can see the question is wrong without knowing what the right one
is. The lead is told none of its options was chosen, and to decide the thing
itself with its evidence or ask a better question. It is a button on the
dashboard, a choice at the terminal, and:

```sh
loadout team gate --think-again
```

Only for a lead's question. A permission or a confirmation is yes or no, and
"think again" there would read as a yes to something nobody had answered.

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

`loadout team daemon` starts the daemon in the background and then shows you
what it says. Close the terminal, or press Ctrl+C, and you stop watching; the
daemon carries on. It used to be the daemon itself, running in that terminal, and
closing the window ended it. A console program ends with its window, and there is
no refusing that, so now it has no window to end with. What it says is kept in
`daemon.log` beside its note in Loadout's state folder, and typing
`loadout team daemon` again shows you the one that is running rather than
starting a second. Where nothing is watching — output going to a file or a
script — it waits only until the daemon says where its dashboard is, prints
that, and returns.

`--foreground` keeps the old way: the daemon runs in the terminal and ends with
it. That is what a service manager or a container wants, since each expects to
own the process it started.

**Tried by hand on Windows only.** On macOS and Linux the daemon leaves the
terminal's session as it starts, so that closing the terminal and Ctrl+C in it do
not reach it. That is covered by nothing but reading the code.

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
folder, started minimised, and that window shows the daemon's log rather than
being the daemon: close it and the daemon carries on. Minimised, so it does not
take focus at every login. On
macOS it is a launch agent, on other Unixes a desktop entry under
`~/.config/autostart`; **neither of those has been logged into**, only the files
they write are covered.

It records the launcher as it was invoked, so run it again after updating
Loadout if the launcher moved.

**Stopping it, holding it, and restarting it** — from any shell, because the
daemon is usually somewhere you are not looking:

```sh
loadout team daemon pause      # no schedule fires; the page stays up
loadout team daemon resume     # 'continue' works too
loadout team daemon stop       # once the runs it started have finished
loadout team daemon restart    # the same, then starts again with the same settings
loadout team daemon stop --now # ends its runs at once, as Ctrl+C does with --foreground
```

A stop waits for the runs the daemon started — from a schedule, the page or a
webhook, since they all run inside it — and starts nothing new meanwhile. A node
mid-turn that is killed loses the turn and what was paid for it, which is the
same reason a run's own stop lands between rounds. `--now` is there for when
you would rather lose that than wait. The command watches for ten seconds and
says whether the daemon has gone or is still finishing.

A hold is only on the clock. Runs already going carry on, and anything started
from the page or a webhook still starts, because that is somebody asking now.
It also outlives the daemon: one held and then stopped starts held next time,
and says so, because stopping the schedules and then rebooting is not a change
of mind. `resume` lifts a hold even with no daemon running. `doctor` says when
a running daemon is paused, since from outside it looks exactly like one that
is working.

A restart is done by the daemon itself, when its runs have finished: it is the
only thing that knows how it was started. The new one keeps the port the old
one was serving, even when that was left to the machine to choose, so a
bookmarked page is still there. The new one runs in the background whichever way
the old one was started, and writes to the same log, so a terminal showing the
old one goes on to show the new one. **Restart has been tried by hand on Windows
only**, not on macOS or Linux, and not on a daemon started at login.

**Finding the dashboard it serves.** The port and the token are both new at
every start, and the daemon prints the address once — into its log, which at
login is shown in a minimised window nobody is looking at. `loadout team dashboard`
gets it back: with a daemon serving, that command prints its address rather than
starting a page of its own. Before that it started a second one, so enabling
autostart and then logging in left you with a dashboard running, no way to reach
it, and a second dashboard offered by the command you would reach for.

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

## Changing this machine from the page

The dashboard has a fold, **What this machine is set to**, carrying the settings
you would otherwise reach through `config set` and the `team` commands: where
notices go, which art the office is drawn with, what may be
started from outside, whether a trigger token exists, what the server listens on,
and which remedies are trusted to run unattended.

Every control types the command you would have typed. Nothing on that page
writes `machines.yaml` — `config set` and the team commands own these settings
and already know what a valid value is, and a second writer would be a second
set of rules to keep in step. A watch-only dashboard draws no fold at all rather
than controls it would refuse.

Three things about it are deliberate.

**Where notices go is never shown.** That address is a webhook address, and a
webhook address *is* the credential: anybody holding it can post into your
channel as you. The page says whether one is held and never what it is, the box
you type it into is emptied the moment it is accepted, and — unlike every other
control here, which prints its whole command line to the terminal serving the
page — a setting change says what was changed and never to what. Naming the
setting and not the value is a rule worth having whole rather than one with an
exception somebody later has to remember.

**Moving the listen address off loopback names what it does, at the moment you
do it.** `0.0.0.0` puts the dashboard on the network you are on, where the token
in its address is the only thing between anyone there and answering your gates,
stopping your runs and starting teams. The command line says that every time it
serves a page; a dropdown that quietly rebound a listener would be the one place
it was not said. It takes effect when the daemon next starts.

**Trusting or revoking a remedy needs the passphrase**, not the token that got
you to the page. It is standing permission for a script to run when nobody is
watching, which is a different act from reading a run — the same reasoning, and
the same credential, as typing at a live node. The refusal is in the server, not
the page, because a page that forgot to ask must still be refused by the thing
holding the port.

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
