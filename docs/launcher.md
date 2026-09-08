# The launcher

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
| `F2` | Settings and paths |
| `F3` | Launch history and posture for the selected project |
| `F4` | Packs, MCP servers, skills and plugins |
| `F10` | Menu |
| `Ctrl+Q` | Quit |

## The launch sheet

Enter on a project does not start the agent. It opens the launch sheet, which
is where everything a launch can be asked is asked, filled in with the
defaults, so Enter again starts the session. That is one keystroke more than
before, and what it buys is the preview.

```text
╭┤Launch loadout-cli├──────────────────────────────────────────────────────╮
│ What are you about to do?                                                │
│ fix the memory import notice                                             │
│ This chooses the specialists the agent is given.                         │
│                                                                          │
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
│ [ Launch ]  [ Cancel ]                                                   │
╰──────────────────────────────────────────────────────────────────────────╯
```

**Agent** lists every agent installed on this machine, the project's own
first. Before the sheet, every launch from the screen started the project's
default agent whatever else was installed, and switching meant quitting and
typing `loadout launch <project> --agent codex`.

**Task and mode** are what choose the specialists. **Profile** and **Worktree**
are offered up front when the project has more than one, by the same names
`--profile` and `--worktree` take, so the sheet and the command line cannot
mean different things.

**This session would load** is the answer the launch itself will get. As you
type the task or change the mode, the sheet asks the same resolver the launch
asks and shows what came back: which specialists, why each was chosen, and
what they cost against the budget. It is not a second implementation of that
choice; it is the choice, shown early. Before the sheet, the only way to see
this was a command that closed the launcher and printed to the terminal.

Cancelling the sheet returns to the list and starts nothing. Launching hands
what was chosen to the `launch` command as its flags, so a session started
from the screen is the same session a typed one is, down to the question about
uncommitted workspace changes on the way out.

![The command palette, listing commands with the one that cannot run from a menu
marked "terminal only"](images/command-palette.svg)

**Ctrl+P reaches everything, and finds it by what it is for.** Searching `undo`
reaches `backup restore`; `broken` reaches `doctor`; `vscode` reaches `code`.
Nobody wanting to undo a mistake searches for the words "backup restore", and a
palette matching only names leaves them believing the capability is absent.

The list is built while the commands are registered rather than written out by
hand, so a command added tomorrow appears without anybody remembering to add it,
and a test asserts the two agree. Commands are grouped by what they are for and
the ones that change files say so, because a palette that looks the same for
reading settings and rewriting them is asking you to remember which is which.

The few that cannot work from a menu — `completion` writes a script to be piped
somewhere, `statusline` is run by the agent several times a minute — are listed
with the reason rather than hidden. Something you cannot find is
indistinguishable from something that does not exist.

![The problems screen: what was found above, what can be put right and what each
fix would change below](images/problems.svg)

**Problems** is a screen of its own: what was found, what can be put right, and
what each fix says it would change, ticked rather than applied as you move
through the list. Space ticks a fix and Enter applies what is ticked; Esc backs
out and changes nothing. Applying with nothing ticked says so rather than
closing, because a screen that closes either way looks the same whether or
not a fix went through. Nothing is applied from that screen — inspecting a repository
and applying a fix are both slow enough that doing them while still drawing
would look like a hang, so the screen collects what was ticked, closes, and the
fixes run with the terminal handed back.

That is the rule the whole launcher follows. Anything needing the terminal for
itself — an agent, a shell, a command's output — happens with the screen closed,
and running a command hands it to the same parser you would have typed at rather
than to a second implementation.

Adding a project stays a sequence of questions rather than a form: it scans the
configured folders or takes a path, registers what you pick, and offers to move
any agent files it finds. That is the same flow first-run setup uses, because
registering a project a fortnight later is the same job.

Changing the workspace repository moves any existing clone aside rather than
reusing or deleting it. The clone belongs to the old repository, so a sync
against a new remote would either fail or, worse, appear to work against the
wrong history.

The launcher is driven end to end in the tests through a headless ANSI driver
that reports back what was actually drawn, so the assertions are about what
somebody would be looking at rather than about text that happened to be
printed.

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

## Cost, history and standing

These live behind a key rather than on the panel, which stays a page where every
line answers a question you'd otherwise type a command for.

- **F3**, or *Launch history and posture* in the menu, runs `loadout launches`
  for the selected project: what was launched, in which posture, and how much
  context each was given.
- *Token usage for this project* runs `loadout usage`.

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
