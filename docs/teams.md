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

## Watching a run

Three views of one thing. Each run writes one journal, and all three read it, so
there is one account of what happened rather than three that can disagree.

- `loadout team status` — where each node got to, what it is doing now, what it
  cost.
- `loadout team dashboard` — a page on this machine, live, at a loopback address
  behind a token that changes every start. It watches and it acts: starting a
  team, answering a gate, holding a run, stopping one and sending the lead a
  message are all buttons on it. Nothing there implements any of that — each button runs the
  command you would have typed, so there is one behaviour rather than two that
  drift.
- **Tools → Team runs…** in the launcher — the same, in the terminal UI. See
  [The launcher](launcher.md).

`loadout team runs` lists what has run; `loadout team log` prints everything one
wrote down, and `--follow` keeps reading as it writes.

### Five ways of looking at the same thing

**List**, **Office**, **Graph**, **Timeline** and **Waiting** across the top of
the runs pane. The first four show the same state and differ only in how you
look at it; the fifth shows what has not become a run yet.

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

The timeline collapses the empty days on purpose. One scale across everything
was tried first and is useless: runs span days and each lasts minutes, so every
bar came out at the minimum width and they all piled against the left edge — a
correct chart that said nothing. Real time is kept *inside* a day, which is
where overlap lives and the only place it matters: two teams running at once
are two teams running at once on an afternoon, never across a week.

**None of them can do anything.** Every control lives in the detail pane, so
answering a gate is implemented once rather than three times. Clicking anybody
anywhere takes you there.

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
```

`room.png` is the office **with nobody in it** — most packs ship an empty or
environment-only variant beside the populated one, and the empty one is the
one to use. The people in the room should be your nodes, not the artist's.

`room.json` says how big the scene is and where somebody stands in it:

```json
{
  "width": 1024,
  "height": 1024,
  "desks": [[20, 24], [13, 47], [31, 47], [49, 47], [13, 66]]
}
```

Each desk is a percentage across and down the scene, so the room draws
correctly at any width. The **lead takes the first desk** and the workers take
the rest in order; anybody the office has no furniture for stands in a row
underneath rather than being left out. A set with no `room.json` draws its
people in a row, which is what every set did before rooms existed.

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
