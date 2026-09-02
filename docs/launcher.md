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

The pictures on this page are drawn by the tests, through the same headless
driver they assert on, so they are the real widgets rather than a drawing of
them. Redraw them after a change with:

```bash
LOADOUT_DOCS_IMAGES=1 dotnet test --filter DocumentationImagesTests
```

Every row says whether you can work on that project, so the list answers the
question without you selecting anything:

| State | Means |
|---|---|
| `+ Ready` | Nothing is in the way |
| `! Attention` | It will launch, and something is worth knowing first |
| `x Blocked` | It will not launch until something is done |

**Blocked is reserved for what genuinely stops a launch** — the repository is
not on this machine, or the agent it wants is not installed here. Committed
agent files, an oversized instruction layer, memory recorded where nothing reads
it, a missing pre-commit hook: all worth fixing, none of them stopping you, so
all of them Attention. A list where everything is blocked says no more than a
list with no states at all, and teaches you to ignore the one project that
really is.

Every state is a word and a mark, never colour alone, so a monochrome terminal
and anyone who cannot tell red from green read the same thing.

The right-hand panel shows what a session would start with — branch, whether the
tree is clean, how much instruction text loads whatever the task, how many rules
stay on demand, how many memory topics exist — and anything wrong with it.

Under the project list, **Recent** shows what you were last doing. Choosing one
reopens that conversation rather than asking again which you meant.

| Key | Does |
|---|---|
| `Enter` | Launch the selected project |
| `Ctrl+P` | Every command the CLI has, filtered as you type |
| `Ctrl+N` | Add a project |
| `F9` | Menu |
| `Ctrl+Q` | Quit |

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
through the list. Nothing is applied from that screen — inspecting a repository
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

