# Using Studio

**At the end:** Studio open in your browser, a session started and typed into
from it, a question from a team run answered, and Studio set up to look the way
you read best.

Studio is a second front end for Loadout, next to the classic dashboard. The
classic page is for watching runs. Studio is where you start sessions, work in
them, and see everything in flight at once. It's a preview: the classic
dashboard stays the default, and both are served by the same daemon, at the
same address, behind the same sign-in.

## Before you start

- Loadout installed and a project registered. See [your first run](first-run.md).
- The daemon running, since it's what hosts Studio's sessions:
  `loadout daemon`. A dashboard started with `loadout dashboard` serves Studio's
  pages too, but can't host a session.
- A browser on the same machine. Studio only answers this machine.

## Steps

### 1. Open Studio signed in

```sh
loadout dashboard --open --studio
```

You should see Studio's Home open in your browser. The command signs the
browser in with a cookie that lasts 90 days, so after this the address alone
works: `http://127.0.0.1:47611/studio/`, or whatever port the daemon is on.

A browser that isn't signed in sees one page, telling it to run that command.
There's no box to paste a token into, on purpose: the sign-in only ever comes
from a terminal on this machine.

To have the daemon's address open Studio rather than the classic page:

```sh
loadout config set dashboard studio
```

The classic page is still at `/classic` either way.

### 2. Find your way round

Down the left is the rail: Home, Sessions, Ideas, Teams, Memory, Context,
Spend, Projects, Commands and Settings, each with a live count. Hover or tab onto it and
it widens over the page without pushing it. Below 600px wide it becomes a
**Pages** tab.

**Ctrl K** (or **Cmd K**) opens the palette from anywhere. It has three groups:
*Go to* a page, find what's *In flight*, and *Do* something. Every action shows
the exact `loadout` command it runs, so you can always do the same thing from a
terminal instead.

Along the top is the mode bar: the five modes, search, and whatever is waiting
on you. Everything here describes Default; [step 16](#16-change-the-whole-layout-with-a-mode)
covers the others.

### 3. Read Home

Home shows everything in flight: team runs, the sessions the daemon is hosting,
ideas being shaped and the tasks you're doing. It has two views, and **Lens**
and **Map** switch between them.

- **Lens** puts one piece of work in the halo, with *Needs you* beside it. A
  failure that wants acknowledging comes first, then the oldest question. The
  most urgent row lifts and the rest recede, and every state is a word and a
  glyph as well as a colour.
- **Map** draws every piece of work as a line through its stages, with *Needs
  you* as the interchange each one passes through. Pick a line and it's drawn
  straight below, with its checks, its last events and what you can do.

**Edit layout** lets you rearrange either view in place. Drag a panel by its
name or use its arrows, hide what you don't use, and **Add back** what you hid.
The layout is kept in Loadout's config (`studio-layout-lens` and
`studio-layout-map`), so it follows you to every browser.

### 4. Answer a question

When a team run stops to ask you something, it appears under *Needs you* with
its answers. A notification appears top right as well, and you can answer from
there. Each answer shows the command it runs:

```sh
loadout team gate <run> --gate <gate> --answer Approve --by dashboard
```

From the keyboard: **Ctrl K**, type a word from the answer, **Enter**.

### 5. Start a session

Press **N**, or **New session…** on the Sessions page. The sheet asks
everything a launch asks:

- the project
- what you're about to do: *No task*, then the tasks the project has recorded
  as doing, open or blocked, then *Other…* for a one-off
- the mode
- the agent
- the profile and worktree, where there's more than one
- the switches `--offline`, `--no-sync` and `--handoff`

Under them, *This session would load* lists the specialists the session would
get and why, with their cost against your instruction budget (12,000 tokens
unless you've changed it). It's worked out by the same resolver as the
terminal's launch sheet and `--dry-run`. The command at the bottom is the
launch, and **Launch** runs it. **Quick session** starts one with no task and
the defaults.

You should see the session open with a live terminal filling the page. Type
into it as you would any terminal: Tab and the arrow keys go to the agent too.
To get the keyboard back out, press **Ctrl ]**, which moves to the **Details**
button. The bar above the screen says so while the terminal has the keyboard.

The bar also has **A−** and **A+** for the text size, and **Fit**, which
shrinks 80 columns to the width of the window. On a phone Fit starts on, and
zooming in scrolls inside the terminal rather than the page. **Details** folds
away the panel beside the terminal; below 960 pixels wide it sits underneath,
folded.

The bar under the screen says what the session needs, in words:

- **Waiting for you**: it has finished and wants your next message.
- **Needs approval**: it's asking to be allowed to do something. The choices
  are in the terminal, and you answer them there.
- **Ended**, **exited with an error** or **lost**, with the
  `loadout resume` command that carries the conversation on in a terminal.

Waiting and approval come only from Claude Code's own hooks, which Loadout
adds to a session Studio starts. Claude Code says it's waiting about a minute
after it finishes, and asks for approval a few seconds after the prompt
appears, so neither shows the instant it happens. Studio never guesses a
question from what's on the screen, and never puts an agent's choices on
buttons: it's told that a question is waiting, not what the choices are.
A Codex session shows only working, idle or unknown, because its own
notifications haven't been checked against OpenAI's documentation yet. The
question clears as soon as you type into the session. Clicking into the
terminal or scrolling back through it doesn't count as an answer.

With the mouse over the terminal, the wheel scrolls the terminal and never the
page. If the agent tracks the mouse, as Claude Code does, the wheel goes to
the agent, which scrolls its own view.

### 6. Leave and come back

The session belongs to the daemon, not the tab. Close the tab, reload, or open
the same session in a second window, and it's still there. Only one window
types at a time; another shows **Type here instead**.

Two things do end it: **End the session**, which asks first, and stopping or
restarting the daemon, which ends every session it hosts. `loadout daemon stop`
warns you when there are any.

To carry a conversation on in a terminal instead, use the command the session
page shows. Once Claude Code has finished its first reply, that names the
conversation itself and its project; before then it's:

```sh
loadout resume --last --project <project>
```

A session that was running when the daemon stopped shows as **lost**, with the
same command, for a week afterwards.

### 7. Make it yours

**Settings → Appearance** has four themes, four accents, three densities and
three levels of motion. The themes are Dark, Light, High contrast dark and High
contrast light; with no theme set, Studio follows your system's colour scheme
and contrast. If your system asks for reduced motion, that always wins, whatever
you choose here.

They're kept in Loadout's config, not the browser:

```sh
loadout config set studio-theme hc-dark
loadout config set studio-accent violet
loadout config set studio-density roomy
loadout config set studio-motion reduced
loadout config set studio-halo gyroscope
loadout config set studio-idle-after 20
```

The things kept in the browser are the single-letter shortcuts (**A** for
Ambient, **N** for a new session), whether Ambient fills the screen, and the
mode with what each mode remembers. They're about the keyboard and screen in
front of you, not about how you read, so a phone can stay in Minimal while a
desktop stays in Terminals.

**Settings** has more than Studio's own. **Loadout's settings** lists every
setting `loadout config` knows, in the same groups the terminal's Settings
screen uses, and each is changed where it's shown: edit it and **Set it** runs
`loadout config set`. Empty clears it back to its default. **Agents and packs**
covers the MCP servers every project loads, secrets (checked and removed here;
stored only in a terminal, at a masked prompt) and specialist packs. **Backups
and safety** covers backups, checkpoints and keeping AI files out of
repositories. **The daemon** holds and restarts the daemon and says whether it
starts when you log in, and **This machine** shows what `loadout doctor` finds.

### 8. Keep what a project remembers

**Memory** lists what each project remembers, by store: this project's, yours
across every project, and this machine's, which is read-only here. Choose a
topic to read it as stored and edit it in place. A save carries the version you
read, so if somebody else changed it meanwhile you see theirs beside yours and
choose, rather than one overwriting the other.

With no topic chosen, the page finds the topics that answer a question, shows
what the memory audit makes of the project, and offers recording a fact,
cleaning up what the audit found, bringing in what the agent recorded outside
the workspace, moving durable facts out of always-loaded instructions, and
rebuilding the index. Cleaning, importing and compressing only report what they
would do until you tick `--apply`. A project topic can be deleted too; a backup
is taken first.

### 9. See what it's costing

**Spend** reads the agents' own transcripts and shows what they've used in
tokens: what was billed as input once the cache had done its work, what was
written, and how much the cache saved, by project, day, model or agent over the
last day, week, 30 or 90 days. Reading a long history takes a while, up to a
minute or so, and the page says it's reading meanwhile.

Below that are the sessions running now, what was launched this week and how
many tokens each started with, the recent sessions, what launched agents reported
about themselves when the receiver was running, and a summary of this machine.

### 10. See what a session will be told

**Context** shows, for one project, what a session would load before it starts.
Type the task in a sentence and choose a mode, and **Explain it** lists each
specialist it would load and why, with the tokens they cost against your budget.
Any specialist opens to show what it says and when it loads.

The other sections cover the project's path-scoped rules and coding styles, its
profiles, whether its documents still describe the repository, and whether the
project departs from what its specialists ask. Each has its actions below it:
splitting an oversized instruction file into rules, choosing a coding style,
drafting or copying a specialist, finding where a type is declared, and writing a
handoff for the next session.

**Your writing voice** shows the voice learned from your own writing and the
rules in force, checks a message or a file against it, and renders it into the
style specialist your sessions load. Learning a voice asks you about each rule
as it goes, so it opens in a terminal here.

### 11. Steer a team run

**Teams** opens on everything a run wants of you, under **Needs you**. A run
lands there when it asks something, when its lead takes a round without asking
for anything, when a node goes quiet for three times its usual turn, or when its
budget is spent or projected to be. Each reason clears itself: nothing is
dismissed by hand. A question is answered where it's shown, and each kind has
its own answers:

- **The lead's question**: its options, the one it recommends drawn solid, and
  **Think again**, which sends it back to the lead.
- **Its readings** of your done-when, before any worker starts: accept them, or
  send them back.
- **A brief**, in a manual run: in a box you can change before it goes. Change
  it and the button says **Send your brief**, and the command gains
  `--instead`.
- **The proposed done-when**: one criterion per line, in a box.
- **A confirmation or a permission**: yes or no.
- **Its budget**: raise it, take the cap off, or type a figure.

Under each answer is the `loadout team gate` command it runs.

Below that are the runs going, each with **Hold after this round**, **Let it
carry on** and **Stop after this round**, and the ones that ended, with **Run it
again** (which opens Start a run filled in, and starts nothing), **Pick it up
where it stopped** and **Forget it**. Then the teams you can run, and the runs on
a schedule, with whether the daemon will fire them.

**Start a run**, **Make a team** and **Run one on a schedule** open a sheet that
writes its command under the form as you fill it in. Start a run asks once more,
naming the team and the goal, because it spends money and edits a project. A
schedule can't be manual, and can't repeat faster than every five minutes.

Choose a run to open its page: its shape (the lead's line, with the nodes each
round briefed hanging from it), what it will be judged on, who is on it, its
spend, a box to say something to the lead, and what you can do to it. Below
are five depths, on keys **1** to **5**:

1. **What it said**: the journal as it's written, or one node's stream.
   Typing at a live node needs the attach passphrase as well as your sign-in;
   it's swapped for a grant that lasts 30 minutes and lives only in the page.
2. **What it cost**: what it's costing, where that's projected to end, where
   the time went and what went wrong.
3. **The papers**: each brief beside the report it got back.
4. **What it changed**: each node's patch, with **Open a pull request**. That
   asks first, and offers **Say what it would do**, which runs it with
   `--dry-run`.
5. **What it delivered**: the run's files in a tree, by node and commit or by
   folder, beside a viewer that shows Markdown rendered, code with line
   numbers, a CSV as a table and an image as itself. **The change** shows the
   file's part of its node's patch. **Download**, **Download all** and **Copy
   path** read only, so they show no command; each file shows the terminal line
   that reads it instead. Lines that look like credentials are taken out before
   a file is shown, and you're told how many.

A team's page has its nodes, what a run of it follows, its runs here, and how
its roles have been doing, read only when you open it. **The bin** puts runs and
teams back, empties for good after naming what goes, and clears out old runs,
saying which as a sentence first. **Remedies** answers a remedy's requests;
trusting a remedy is a standing agreement, so the page shows the command for a
terminal instead. So does setting or clearing the attach passphrase, since the
page can't set the passphrase that guards it. Naming a run's room stays on the
classic dashboard, which is the one that draws the office.

### 12. Shape an idea

**Ideas** lists every idea being shaped, by project, and marks the ones with
questions waiting for you. Choose one and answer its questions in the page:
pick one of the agent's options or say it in your own words, then **Send answers
and refine** to have an agent take it one round further, or **Only save the
answers** to come back to it. A round runs on the daemon, so you can leave the
page; a toast says when it's finished.

Once there's a plan, each layer shows its options with what's for and against
them. **Choose this**, **Keep it** and **Drop it** each run the command shown
under them, and a layer, an addition or the whole plan can be sent back with a
request. **Accept the plan** writes it out and turns the idea into a task on its
project.

**Notes kept elsewhere** takes notes you've pasted from somewhere else and has an
agent split them into ideas and tasks. Nothing is recorded until you apply the
pieces.

### 13. Look after your projects

**Projects** lists the projects this machine knows. Choose one to see where it
is, its branch, its agent and how often it's been launched, its working trees,
and its health: where it has drifted from what was recorded, and whether any AI
tooling files are tracked in the repository. Below that is everything you can do
to it, each folded until you open it: open it in the editor or its folder, change
what every session gets, put right the drift that can be put right, relocate it,
link it, or stop knowing it. Each shows the command it runs, and what's shown is
read again once it finishes.

A project's **Tasks** list what is being worked on, each moved on with one press
(start it, mark it done, say it's blocked, drop it), and new tasks are recorded
below them. Done and dropped ones are left out.

**More** has the rest: adding a repository you have, making a new project,
finding repositories and stray memory, tasks that belong to no project yet, and
the workspace, where you can bring in
what other machines saved and save and push what changed here.

### 14. Run anything else from Commands

**Commands** lists every command Loadout has, folded by area. Choose one and
Studio builds a form from the command's own options, so it can't offer one the
command doesn't take, and shows the line it'll run as you fill it in. **Run**
runs exactly that line on the daemon; **Preview** adds `--dry-run` for a command
that changes something. What it printed comes back on the page, with any secret
taken out, and a toast says when it's finished.

It's how you reach the parts of Loadout that don't have a page of their own yet.
Each command runs from your home folder, so a command that picks its project
from the folder you're in needs `--project`. It's never asked a question: a
command that would ask one is refused instead, and says so.

A command that asks as it goes, such as `setup`, `voice learn`, `memory review`
or `resume`, can't be run that way, because nobody could answer it. Its form has
**Open it in a terminal** instead, which runs it in a terminal the daemon hosts
and takes you there, where you answer it as you would in your own.

A few commands only ever show their line, with why: setting a secret (it asks
for the value at a masked prompt), the attach passphrase, trusting a tool or a
remedy, `update`, and the commands that are themselves a server or a status
line. Starting a session sends you to **Sessions**, where Studio hosts it.

### 15. Ambient

On Home, **A** or **Ambient** turns Studio into something for a spare screen:
the key numbers, the halo on whatever most needs you, and everything else
circling it. **Escape** or **Leave ambient** leaves it.

### 16. Change the whole layout with a mode

The mode bar at the top of every page has five modes, and **Ctrl Shift M**
moves to the next from anywhere, a terminal included. The palette lists each
one as *Mode: Terminals* and so on. The mode is kept in this browser, not in
Loadout's config.

- **Default** is Studio as the rest of this guide describes it.
- **Terminals** turns Home, Sessions and a session's page into one deck: every
  hosted session a tab, over one, two or four live panes. **Ctrl Alt 1** to
  **9** picks a session by its tab, and **Ctrl Alt** with the arrows moves
  between the panes shown. A dot on a tab means output since you looked. **Go
  to the one waiting** appears when a session needs you, and *Find in this
  terminal* searches the one you're in, highlighting matches in the needs
  colour. Below 900px it's one pane and the tabs. The layout and the last
  session you picked are remembered.
- **Teams** turns Home and the Teams page into a board: each run one row with
  its goal, a small line of its rounds and nodes, its state and its spend, the
  one that needs you first, with its question answered in the row. The teams
  run along the top. Picking a run or a team opens its page beside the board;
  below 1100px it replaces the board, and **Close** goes back. Remedies, the
  bin, shared tools and the webhook stay on the Teams page in Default.
- **Minimal** is one quiet column of what's live, sessions and runs together,
  what needs you first with its question under it. Opening one gives it the
  screen: a session as just its terminal, a run as just its question, its shape
  and its done-when. **Escape** or **Back** gives the column back. The rail
  slides away; the mode bar stays.
- **Pure** is [step 17](#17-pure-focus).

Whatever the mode, the count on the right of the mode bar is everything waiting
on you, sessions and runs together, and pressing it goes to the oldest question
wherever that mode shows it. No mode hides a question.

Changing mode is one animated step where your browser can do it, a short fade
under reduced motion, and simply happens under none.

Each pane in Terminals is its own connection to the session. A pane you stop
showing closes its connection; show it again and the daemon replays what the
session has written, so nothing is missed. Typing goes only to the pane that
has the keyboard, and **Ctrl ]** gives the keyboard back to its tab.

### 17. Pure Focus

Pure Focus is for acting fast, with the keyboard or the mouse. It's flat: no
glow, nothing that breathes or pulses, 1px edges, in your own theme and accent.
The only movement is a brief flash on what you ran.

Home, Sessions, a session, Teams and a run all become one desk of three panes
over a command bar:

- **List**, on the left: what needs you, then sessions, team runs, ideas and
  tasks.
- **View**, in the middle: the thing you picked. A session is its terminal; a
  run is its question, its shape, its done-when and its crew; an idea or a task
  is its facts and latest events.
- **Actions**, on the right: up to nine, numbered, each with the command it
  runs. They're the same actions, with the same commands, as anywhere else.

Every key is printed beside what it does, and **?** lists them all:

| Key | Does |
|---|---|
| **J**, **K**, or the arrows on the list | move the selection |
| **Home**, **End** | first, last |
| **Enter** | open what's picked in the view |
| **Escape** | back to the list; keeps an armed action |
| **1** to **9** | run that action |
| **/** | filter the list |
| **:** | open the command bar |
| **G** then **H**, **S**, **T**, **I**, **K** or **N** | the list: everything, sessions, team runs, ideas, tasks, what needs you |
| **F6**, **Shift F6** | the next or previous pane |
| **?** | every key |

Single keys work only when you're not typing. In a terminal every key goes to
the agent, and **Ctrl ]** brings you back to the list. With single-letter
shortcuts off, only **Escape** and the list's own arrows, **Home**, **End** and
**Enter** work; **F6**, the buttons and the command bar still do.

An action that can't be undone, such as **Stop after this round** or **End the
session**, asks for its number twice. The first press says *Press 5 again to
stop after this round* for three seconds, and **Escape** keeps it. There's no
dialog, which is deliberate: a dialog would take the keyboard away from the
desk.

The command bar finds the picked thing's actions, places and things, and every
command in Studio's catalogue, each showing what it runs before you press
**Enter**. A command opens its form on the Commands page, in Default.

The scope and the last thing you picked are remembered. A team's page, the
remedies and the bin aren't part of the desk; they open as pages, in the flat
look.

## Watching only

```sh
loadout dashboard --watch-only
```

Studio still shows everything, but changes nothing. A banner says so, and every
action shows the command to type in a terminal instead. Launching, typing into a
session, ending one and changing a setting are all refused by the server as
well as the page. On **Commands**, only commands that just read run, and
**Preview** still works for the rest.

## What it won't do

- **Read a secret back, or make an agreement for you.** The team attach
  passphrase and a tool's test agreement are set in a terminal. Settings shows
  the commands, never a button.
- **Reach another machine.** Studio answers loopback only, and checks that every
  change comes from its own page.
- **Write terminal output to the daemon's log.** What an agent prints stays in
  the session.

## If something goes wrong

- **"This browser isn't signed in to Studio"** — run `loadout dashboard --open
  --studio` again. Somebody may have signed every browser out
  (`loadout dashboard sign-out`, or **Sign out every browser** in Settings).
- **"Only the daemon hosts sessions"** — you opened Studio from
  `loadout dashboard`. Start the daemon with `loadout daemon` and open its
  address instead.
- **A session has gone** — the daemon restarted. Sessions don't survive that;
  `loadout resume --last` carries the conversation on in a terminal.
- **Anything else** — **Report a problem**, at the foot of the rail, opens
  Studio's bug form on GitHub with the version and platform filled in. It sends
  nothing on its own; paste `loadout doctor` into the form if you can. Studio is
  a preview, and these reports decide when it becomes the default.
