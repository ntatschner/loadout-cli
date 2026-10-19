# The launcher

*Want the steps in order? [Launching a session](guides/launching.md) walks through the launch sheet one step at a time.*

Running `loadout` with no arguments opens a full-screen launcher: the project
list on the left, everything known about the selected project on the right, a
filter you can type into, and a menu naming what the launcher can do.

![The launcher: a project list on the left, the selected project's detail on the
right](images/launcher.svg)

```bash
loadout                    # the launcher
loadout starstats          # skip it and launch that project
loadout here               # skip it and launch whatever repository you are in
```

The screen is quiet on purpose. The frame you are in is the warm one and every
other frame is grey, so the eye finds the focus without a bar of colour; the
detail pane sits a shade above the ground with no line round it; and the keys
along the bottom are picked out from what they do. The list's title carries a
count, and says how much of the registry a filter is showing. None of that is
load-bearing: the same words and marks are there in a terminal with no colour
at all.

The build you're running sits in the top-right corner, on the menu bar's own
row. It's there because the `loadout` on your PATH can fall behind the one you
think you're running — three releases behind, on one occasion, with the feature
being discussed missing from the binary and nothing on screen saying so.

When there's something newer it says so too: `v1.2.0 · 1.3.0 available`. That
lookup never delays the launcher opening and never puts a failed check on
screen. It runs once in the background per session, the answer is cached for a
day against the version that asked for it — so the build that replaces yours
can't inherit a cache announcing itself — and a failure is cached as "nothing"
so an offline machine asks once a day rather than on every start. No command is
offered beside it: the corner of a screen is no place to teach one, and
`loadout update` is in the menu under Tools. `loadout config set updates-source
off` switches the lookup itself off, not just the mention.

The pictures here are drawn by the tests, using the same headless driver they
assert on, so they're the real widgets rather than someone's drawing of them.
Redraw them after a change with:

```bash
LOADOUT_DOCS_IMAGES=1 dotnet test --filter DocumentationImagesTests
```

A row carries a mark only when something genuinely stops a launch: the
repository is not on this machine, or the agent it wants is not installed here.
Everything else about a project — committed agent files, an oversized
instruction layer, memory recorded where nothing reads it, a missing pre-commit
hook — is worth fixing and stops nothing, so it goes in the panel on the right
under *Needs attention* rather than on the row. A list where every row carries
a warning says no more than a list with none, and teaches you to ignore the one
project that really is blocked.

Every state is a word and a mark, never colour alone, so a monochrome terminal
and anyone who cannot tell red from green read the same thing.

The right-hand panel shows what a session would start with — branch, whether the
tree is clean, how much instruction text loads whatever the task, how many rules
stay on demand, how many memory topics exist — and anything wrong with it.

Under the project list, **Recent** shows what you were last doing. Choosing one
reopens that conversation rather than asking again which you meant.

| Key | Does |
|---|---|
| `Enter` | Open the launch sheet for the selected project |
| `Ctrl+P` | Every command the CLI has, filtered as you type |
| `Ctrl+N` | Add a project |
| `Delete` | Take the selected project off the list, on every machine that shares your workspace. It asks first, and never touches the repository |
| `F2` | Settings and paths |
| `F3` | Launch history and posture for the selected project |
| `F4` | Packs, MCP servers, skills and plugins |
| `F10` | Menu |
| `Ctrl+Q` | Quit |

## The launch sheet

Enter on a project doesn't start the agent. It opens the launch sheet, where
every question a launch can ask is already asked and filled in with the
defaults, so Enter again starts the session. That's one keystroke for the
preview, which is a good trade.

```text
╭┤Launch loadout-cli├──────────────────────────────────────────────────────╮
│ What are you about to do?                                                │
│ No task                                                                  │
│ doing    Fix the memory import notice                                    │
│ open     Add value examples to specialist docs                           │
│ Agent          Mode                    Profile            Worktree       │
│ claude         (let the task decide)   default            main (main …)  │
│ codex          advise                                     feature-x      │
│                implement                                                 │
│                investigate                                               │
│                review                                                    │
│                                                                          │
│ [ ] Work offline  [ ] Skip workspace sync  [ ] Append last handoff       │
│╭ This session would load ───────────────────────────────────────────────╮│
││ foundation   Change safety, Engineering core, Evidence first, …        ││
││ mode         Implement                  implement mode                 ││
││ language     C#                         459 .cs files                  ││
││ framework    .NET                       Microsoft.Extensions. declared ││
││                                                                        ││
││ specialists  about 1,456 tokens, 12% of 12,000                         ││
│╰────────────────────────────────────────────────────────────────────────╯│
│ [ Launch ]  [ Quick session ]  [ Cancel ]                                │
╰──────────────────────────────────────────────────────────────────────────╯
```

**Agent** lists every agent installed on this machine, the project's own first,
so switching is a keystroke rather than a quit and a retyped command line.

**The task is picked from what the project has recorded, not typed.** It's the
choice that decides most of what a session is given, and when it was a blank
line nobody filled it in: across 122 recorded launches here it was typed twice,
both the same sentence. So the sheet lists what `loadout task declare` already
recorded — work in progress first, then what hasn't been started, then what's
waiting, newest first within each — between *No task* at the top and *Other…*
at the bottom. Every one of them, scrolling, not the first few. Finished and
abandoned work is left out, because the list describes what a session is about
to do.

It opens on the newest task you're working on, or on *No task* when you're not
working on anything. It doesn't guess at an open one: a guess that chooses the
specialists is one you'd never see being made.

Picking a task links the session to it. The task's title chooses the
specialists, exactly as typing it would, and the session is also told which
task it's for: its note goes first in what the agent reads, under *This
session's task*, and isn't repeated in the list of open tasks after it. The
launch ledger records the task's id, so you can tell later which session was for
what. Linking doesn't change the task's state; you still move it on yourself.

*No task* leaves the specialists to the repository alone. The list of open tasks
is still in the context, so the agent can still see what the project is working
on. *Other…* (Enter on it) swaps the list for a line you can type a one-off task
into; Esc on an empty line puts the list back.

Enter on a task launches. A click only selects it, because choosing a row isn't
asking to start a session.

**Quick session** launches straight away with no task and the defaults,
whatever you've moved on the sheet. Use it for a session that's a question
rather than a piece of work.

**Task and mode** are what choose the specialists. **Profile** and **Worktree**
show up front when the project has more than one, under the same names
`--profile` and `--worktree` take, so the sheet and the command line can't mean
different things.

**This session would load** is the answer the launch itself will get. As you
pick or type the task or change the mode, the sheet asks the same resolver the launch
asks, and shows you what came back: which specialists, why each one was chosen,
and what they cost against the budget. It isn't a second implementation of that
choice. It's the choice, shown early, without closing the launcher to see it.

Cancel the sheet and you're back at the list with nothing started. Launch and it
hands what you chose to the `launch` command as its flags (a picked task as
`--task-id`), so a session started
from the screen is the same session a typed one is — down to the question about
uncommitted workspace changes on the way out.

![The command palette, listing commands with the one that cannot run from a menu
marked "terminal only"](images/command-palette.svg)

**Ctrl+P reaches everything, and finds it by what it's for.** Search `undo` and
you get `backup restore`; `broken` gets you `doctor`; `vscode` gets you `code`.
Nobody trying to undo a mistake searches for the words "backup restore", and a
palette that matches names alone just convinces them the capability isn't there.

The list is built as the commands are registered rather than written out by
hand, so a command added tomorrow turns up without anybody remembering to add
it, and a test asserts the two agree. Commands are grouped by what they're for,
and the ones that change files say so — a palette that looks identical for
reading settings and rewriting them is asking you to remember which is which.

Type two characters or more and the last row searches everything for them:
tasks, ideas, sessions, team runs and projects, as `loadout find` does. It's
last so that typing a command's name still lands on the command, and a word no
command matches still finds something.

The few that can't work from a menu are listed with the reason rather than
hidden: `completion` writes a script you pipe somewhere, `statusline` gets run
by the agent several times a minute. Something you can't find looks exactly like
something that doesn't exist.

![The problems screen: what was found above, what can be put right and what each
fix would change below](images/problems.svg)

**Problems** is a screen of its own: what was found, what can be put right, and
what each fix says it would change — ticked as you move through the list, not
applied. Space ticks a fix, Enter applies what's ticked, Esc backs out and
changes nothing. Apply with nothing ticked and it tells you, rather than just
closing, because a screen that closes either way looks the same whether or not a
fix went through. Nothing actually runs from that screen. Inspecting a
repository and applying a fix are both slow enough that doing either mid-draw
would look like a hang, so the screen collects what you ticked, closes, and the
fixes run with the terminal handed back.

That's the rule the whole launcher follows. Anything that needs the terminal for
itself — an agent, a shell, a command's output — happens with the screen closed.
And running a command hands it to the same parser you'd have typed at, not to a
second implementation.

Adding a project stays a sequence of questions rather than a form. It scans the
folders you configured or takes a path, registers what you pick, and offers to
move any agent files it finds. That's the same flow first-run setup uses,
because registering a project a fortnight later is the same job.

Changing the workspace repository moves any existing clone aside instead of
reusing or deleting it. The clone belongs to the old repository, so syncing it
against a new remote would either fail or — worse — look like it worked, against
the wrong history.

The tests drive the launcher end to end through a headless ANSI driver that
reports what was actually drawn, so the assertions are about what you'd be
looking at rather than text that happened to get printed.

## Sessions already running

The panel's detail pane says so when something is already open against the
selected project — "a session is running here". It's the one line there that
changes what you'd do next rather than describing how things stand: launching a
second agent into a repository you're already working in is the mistake it
exists to prevent.

It is **not** filed under "needs attention". A session running is a fact about
right now, not a problem with the project, and putting it there would train you
to skim that list.

Nothing is said when nothing is running. A line reading "0 sessions" would cost
attention on every project, every time.

## Watching a team run

*Team runs…* in the Tools menu opens what the runs on this machine are doing,
while they do it. It's the same account the dashboard serves and `team status`
prints — each run writes one file and all three read it, so there's one record
of what happened and three ways to look at it rather than three things that can
disagree.

The list is every run, newest first, with where each got to written as a word.
Below it, the selected run's nodes: which role, what state, how many turns, and
what the node is doing right now. A ceiling on how much longer it might take is
shown when there's something to base one on, and said as a ceiling — what a lead
asks for next isn't known to anybody, so anything phrased as a prediction would
be one.

Nothing is started or stopped from here. **Enter** opens the run's log, **s**
its status, **d** the dashboard — each by running the command you'd otherwise
have typed.

**r** forgets the run the cursor is on, and is the one key here that changes
anything, so it's the one that asks first: it names the run and defaults to
no. Saying yes runs `team runs remove`, which is also what refuses a run that
hasn't finished — those aren't offered at all.

It reads again every two seconds. Under a profile that asked for no redraws or
for less motion it doesn't, because a screen repainting itself every two seconds
is what that setting exists to refuse: a screen reader is handed the whole list
again on every pass and never reaches the end of it. **F5** reads once, either
way.

A run starting while you're watching arrives at the top and pushes the rest
down. The cursor stays on the run it was on, by identifier rather than by
position, so the list moving doesn't move you onto something else.

## Working on a project's tasks

*Work on its tasks…* in the Project menu lists the selected project's tasks,
open first, then doing and blocked, and starts a session on one.

| Key | Does |
|---|---|
| **Enter** | Start a session on the task, the same as `loadout launch <project> --task-id <id>` |
| **o** | Start with options: the launch sheet, opened on this task |
| **/** | Filter, with the query `task list --filter` takes, such as `state:open login` |
| **s** | Change the order: newest first, by id, by title, then back to the default |
| **a** | Show everything, done and dropped included |
| **r** | Back to the default, which hides done and dropped work |

An open or blocked task moves to doing as the session starts. One already doing
or done keeps its state, and a dropped one can't be started at all, so **Enter**
on it does nothing. The line under the list says what the filter is and how
many tasks it's hiding, and a filter the list can't take is refused there in
the same words Studio and the terminal use.

The list opens with the filter saved for it, which is the one you last set in
Studio. Changing it here doesn't save it. To change the saved one from a
terminal, use `loadout config set studio-query-tasks --value=<query>`.

## Working on ideas

The Ideas menu has two ways in:

- *Dump notes…* takes notes you kept somewhere else. Paste them in and an agent
  splits them into separate ideas and tasks, each quoting its own words from
  the notes. You get the pieces as a list, all ticked, and untick what you
  don't want before anything is recorded.
- *Work on ideas…* opens every idea, on every project and the workspace-wide
  list. The selected one is shown in full underneath: its questions and your
  answers, then the plan, a layer at a time, with the options, the one chosen
  and what you've said about each piece. **Tab** moves into that pane to
  scroll a long plan, and the keys work from there too.

The keys follow the order you'd work through an idea:

| Key | Does |
|---|---|
| **n** | Drop a new idea in, on the project selected in the launcher |
| **r** | Have the agent take it a round further: ask its questions, propose a plan, or revise one |
| **a** | Answer a question: pick one of the agent's likely answers, or type your own |
| **c** | Choose an option for a layer |
| **k**, **d** | Keep or drop pieces of the plan. An addition is only in the plan once kept |
| **i** | Ask for a piece, or the whole plan, to be reworked in the next round |
| **y** | Accept the plan and choose where the work goes |
| **Del** | Forget the idea, after asking |

Each key runs the command you'd otherwise have typed (`idea answer`,
`idea choose` and the rest) and then opens the screen again on the same idea,
read afresh. The screen never has its own copy of what the commands do.

A key that the command would only refuse does nothing. **r** does nothing while
questions are waiting, because a round run then would be asked the same
questions again. **y** does nothing until the plan has taken in everything you
asked of it. That's quieter than closing the screen just to be told no.

A round takes a minute or two and prints what it asked or proposed, so the
screen waits for a key before coming back. Answering or marking a piece prints
a single line saying it worked, so the screen comes straight back: stopping for
each of those would make working through a plan a chore.

Accepting offers the project the idea was dropped on first, then every other
registered project, then a new project named after the plan. Making a project
is always your choice. The agent's opinion of where the work belongs is shown,
but never acted on by itself.

Answers and requests are passed to the command as `--answer=…` and
`--request=…`, not as bare arguments. The parser refuses any value that
starts with a dash, and a bulleted list pasted into an answer would otherwise
fail with "Option does not have a name".

Not here yet: the text launcher that screen readers use has no ideas screen,
so there the commands are the way in.

## Cost, history and standing

These live behind a key rather than on the panel, which stays a page where every
line answers a question you'd otherwise type a command for.

- **F3**, or *Launch history and posture* in the menu, runs `loadout launches`
  for the selected project: what was launched, in which posture, and how much
  context each was given.
- *Token usage for this project* runs `loadout usage`.
- *Carry open tasks into sessions* and *Inline the code map into sessions* run
  `loadout project context`, flipping whichever switch the panel is showing.
  The Context line says what a session is given beyond its instructions, so
  the state the entry flips is on screen next to it rather than remembered.

What the key runs is the command itself, not a copy of it. A screen never
implements command behaviour here, or there are two implementations and one of
them drifts.

F3 has been pressed on a real Windows console and returned the project's
launch history. The menu item reaches the same command for anyone whose
terminal eats function keys.

The launch sheet has not yet been used on a real console. Its keystrokes and
what it draws are tested through the headless driver at 80x24 and above, but
whether it is comfortable to use is not something a test can say. Worth trying
once and telling me what is awkward.
