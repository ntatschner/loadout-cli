# Commands

| Command | Purpose |
|---|---|
| `loadout` | Full-screen launcher, or first-run setup |
| `loadout setup` | Configure the launcher on this machine |
| `loadout <project>` | Launch the project's default agent |
| `loadout here` | Launch the agent for the current repository |
| `loadout doctor` | Platform, Git, workspace, secret and agent diagnostics |
| `loadout status` | Summary of workspace, projects and agents |
| `loadout project add\|list\|| `loadout instructions list|show|explain` | Read the specialists, and see which ones a task would load |
| `loadout instructions explain --against-mode|--against-task` | Show only what changes between two ways of asking ||remove\|discover\|open` | Manage project registration |
| `loadout project clone\|relocate <project>` | Get a registered project onto this machine |
| `loadout project survey [--adopt]` | Find agent state no project accounts for, and take on what it can |
| `loadout project link [project]` | Record inside a repository which project it belongs to |
| `loadout code [project]` | Open a project in the editor, under the profile its agent uses |
| `loadout config list\|get\|set\|edit` | Read and write launcher settings, and say where they live |
| `loadout workspace status\|sync\|save\|open` | Manage the central workspace clone |
| `loadout desktop` | Install the Start Menu or `.desktop` entry |
| `loadout update` | Check the release source and install a newer build |
| `loadout secret set\|test\|remove` | Manage credentials in the OS keystore |
| `loadout mcp list\|add\|remove` | Manage the MCP servers a project loads, and see what clashes |
| `loadout mcp serve` | Serve the launcher's own tools to an agent. Started by the agent, not by you |
| `loadout repo check` | Check a repository for tracked AI tooling files |
| `loadout drift [project]` | Show where projects have drifted from their recorded configuration |
| `loadout drift --fix` | Put right the drift the launcher can fix itself |
| `loadout doctor --fix` | Put right the findings the doctor can fix itself |
| `loadout doctor --bundle [path]` | Write the findings to one file to send somebody, screened first |
| `loadout docs audit [project]` | Report where the documentation has come adrift from the repository |
| `loadout protect` | Install a pre-commit hook, or `--global` Git excludes |
| `loadout migrate` | Move existing AI tooling files into the workspace |
| `loadout project worktrees <project>` | List a project's working trees |
| `loadout handoff <project>` | Create, show or list cross-agent handoffs |
| `loadout profile list <project>` | Show a project's context profiles |
| `loadout rules list\|budget\|audit <project>` | Inspect the instruction rules and what they cost |
| `loadout rules split <project>` | Break an oversized instruction file into scoped rules |
| `loadout memory list\|write\|audit\|reindex <project>` | Record and check durable project facts |
| `loadout memory import <project>` | Bring in memory an agent recorded outside the workspace |
| `loadout memory audit --clean <project>` | Remove empty topics, exact repeats and dead index lines |
| `loadout memory find <query>` | Find the topics that answer a question, rather than reading the index |
| `loadout memory write --separate` | Start a new topic even though existing ones cover similar ground |
| `loadout memory write --scope user|machine` | Record a fact true of your work, or of this computer only |
| `loadout memory compress <project>` | Move durable facts out of always-loaded instructions into memory |
| `loadout sessions` | List recent agent sessions across every agent, newest first |
| `loadout launches [project]` | What this machine launched, and what each launch was given |
| `loadout resume [session]` | Reopen a previous session, with a picker when none is named |
| `loadout instructions list\|show\|explain` | Read the specialists, and see which ones a task would load |
| `loadout instructions audit\|validate` | Check a project against what its specialists ask for, or check the library itself |
| `loadout instructions new <id>` | Draft a specialist or skill in the workspace, or in one project |
| `loadout instructions stats` | Say which specialists launches actually reached, and which none did |
| `loadout usage [--days|--by|--project]` | What the agents have spent, by project, day, model or agent |
| `loadout usage --format markdown|csv` | The same report written to send somebody, or to open in a spreadsheet ||--by\|--project]` | What the agents have spent, by project, day, model or agent |
| `loadout telemetry serve\|status` | Receive, locally, what launched agents report about their own usage |
| `loadout statusline install\|uninstall\|show` | Put the project, branch and context spent in the agent's status line |
| `loadout backup list\|restore` | Undo an operation that changed files |
| `loadout completion <shell>` | Emit a completion script |

**Every command that can change something accepts `--dry-run`,** and it always
means the same thing: show what would happen and change nothing. Several
commands have their own older spelling — `--apply` on some, `--fix` on others —
and those still work; where both are given, the more cautious wins.

Every command accepts `--json`, and everything after a bare `--` is passed to
the agent untouched:

```bash
loadout starstats --agent claude --profile database -- --verbose
```

Exit codes are stable and documented in
[`ExitCode.cs`](../src/Loadout.Models/ExitCode.cs).

## Editors

VS Code keeps settings, extensions and keybindings in named profiles, and
working with an agent usually wants a different set from working without one.
`loadout code` opens a project under the profile that suits it, so the same
repository opened for Claude and opened for Codex can put the editor in two
different states.

```yaml
# config.yaml
editor:
  command: code          # or code-insiders, codium, cursor
  profiles:
    claude: Agents
    codex: Codex
```

The profile used is the project's own if it names one, then the one configured
for the agent it uses, then none — so if you do not use profiles you get the
editor you always get.

```bash
loadout code                            # this repository, its agent's profile
loadout code starstats --agent codex    # that project, as Codex would want it
loadout code --editor-profile Agents    # this profile, whatever is configured
```

`--editor-profile`, not `--profile`: that one already exists and means a context
profile — which instructions an agent loads — and has nothing to do with the
editor.

Profiles are opened, never written. Their contents live in a layout the editor
does not publish, and rewriting it would be a promise that could not be kept
across editor versions. Reading it to report on it is a different matter, so
`loadout doctor` says which editor was found and which profiles exist, and warns
when a project or an agent names one that does not. Where the profiles cannot be
read at all it says so rather than reporting them as missing — a wrong "that
does not exist" sends you looking for a problem you do not have.

## Sessions

Each agent records its own conversations in its own private layout, and neither
can say which project a session belonged to. `loadout sessions` reads both and
attributes them:

```text
1 minute ago   claude storefront-api     Fix the upload path
5 minutes ago  claude storefront-web     Redesign the settings screen
2 days ago     codex  storefront-api     Tidy the deploy script
```

`loadout resume` opens a picker, or takes a session id or `--last`. Resuming
goes through the launcher rather than the agent directly, so the workspace
synchronises and the context recompiles instead of a bare transcript being
reopened. The interactive launcher offers the same picker per project.

Neither storage format is a published contract, so both readers are
best-effort by construction: a transcript that cannot be understood costs that
one session and never the listing.

## Launches, which are not sessions

`loadout sessions` reads the transcripts the agents write and says what a
conversation was about. `loadout launches` reads what this launcher recorded as
it started one, and says what it was told to be — the mode, the profile, the task
and every specialist composed into it.

```console
$ loadout launches --show aa11
StarStats  aa11bb22

  Started       2026-09-04 00:31:42Z
  Agent         claude
  Mode          implement
  Outcome       ok
  Ran for       31 minutes
  Task          fix the upload path
  Instructions  2,400 estimated token(s) against a budget of 12,000

Composed  3
  foundation.change-safety
  foundation.verification
  language.csharp
```

They are separate lists on purpose, and neither can be turned into the other: an
agent picks its own session identifier and the launcher never learns it. Nor
does this say what a launch spent — token counts are aggregated by directory and
day, so attributing them to one of three launches that day would be arithmetic
dressed as fact. The tokens shown are the instruction tokens the launcher
estimated and recorded, which really are per launch.

A launch has three outcomes rather than two. `unclosed` means no ending was
recorded — killed, terminal closed, or still going — and `never ran` means it
ended without the agent starting. Calling either a failure would invent a result
neither has.

## Documentation that still describes the code

Loadout has checked its own documentation for a while, by hand, three times
over: a test that every command the docs name exists, one that the install
examples name the version that ships, one that the specialist count is the
count. Each was written after the drift it now catches — a table left naming the
old sub-commands, a count left at 71, a download link left at 0.9.2 through five
releases. `loadout docs audit` is that habit offered to any repository.

```console
$ loadout docs audit
+ 20 document(s), every reference resolves.
```

It reads `docs/` and the Markdown beside the root README, and reports three
things: a link that goes nowhere, a named file that isn't in the repository, and
a page nothing links to. It reports and changes nothing — what to do about a
stale page is a judgement about a codebase.

What it deliberately stays quiet about is the more interesting half. A URL, an
anchor, a home-relative path and anything holding a `<placeholder>` are all left
alone. So is a backticked path that doesn't start at a directory the repository
actually has: pointed at this project first time round it produced seven
findings and every one was wrong — an invented Rust path inside a paragraph about
deriving globs, and a table addressing real files relative to `src/<project>`
rather than the root. A check that's wrong about seven good references and right
about none is one you turn off.

### Numbers in prose

A project can also say which of its numbers have a right answer. The policy
lives in the workspace, at `projects/<slug>/docs.yaml`, so the repository keeps
holding source and the rules about it live elsewhere:

```yaml
root: docs
counts:
  specialist: "src/Loadout.Core/Specialists/**/*.md"
counts_exclude:
  # A survey of a proposed external bundle, written before implementation.
  # Its numbers are about that bundle, not about this repository.
  - specialists-architecture.md
```

This is the drift that rots invisibly, because the sentence still reads
perfectly. "There are 73 specialists" sat at 71 while the library grew, and
nothing about the page looked wrong. Keyed by the singular; the plural is
derived, because writing both out is configuration nobody keeps in step. The
number one is never read as a total — "the full text of one specialist" is a
quantity in a sentence, and prose is full of them.

`counts_exclude` is there because counting assumes a noun means the same thing on
every page, and sometimes it doesn't. Without it, this project's own
`specialists-architecture.md` reports as stale on every number it contains: it's
a survey of somebody else's library, and every one of those numbers is correct
about that library. A project with no policy still gets all the checks above,
which need nothing configured.

## Saving what a session produced

Agents change workspace files during a session — context notes, decisions,
handoffs. When the agent exits, the launcher applies the exit policy from
`config.yaml`:

| `sync-exit` | Behaviour |
|---|---|
| `prompt` (default) | Offers save-and-sync, save-locally, review, or leave |
| `always` | Commits and pushes without asking |
| `never` | Leaves the changes alone |

Whatever the policy, the changes are screened before anything is committed. The
workspace is a Git repository that gets pushed — under `always`, without anybody
being asked — so a credential reaching it is a credential disclosed, and an audit
finding afterwards doesn't undo that. A change that looks like it carries one
refuses the save and names the file and the pattern, never the value:

```text
The workspace was not saved. These changes look like they carry credentials,
and the workspace is a Git repository that gets pushed:
  projects/starstats/handoffs/2026-02-01.md — GitHub token
Take the value out and put it in the credential store with 'loadout secret set',
then save again.
```

Memory has been screened at the point of writing since it existed. This is the
same answer applied to everything else the policy commits — handoffs, project
instructions, context notes, profiles, MCP definitions — and to anything an agent
wrote into the workspace directly, which no check at the point of writing can
see. Binary files are left alone; a file that can't be read is reported rather
than passed, because "clean" is the one thing a scan must not say about
something it never opened.

Commits follow the format in spec section 46, so a workspace history reads as a
record of which machine did what:

```text
agent-workspace: update StarStats context

Project: StarStats
Agent: claude
Machine: DEV-PC
```

A session that only read produces no commit. If a push fails the commit has
already happened, and the message says so rather than implying the work went
nowhere. The fourth option is deliberately "leave them uncommitted" rather than
"discard": the launcher has no business deleting work somebody just did.

The prompt lives in the CLI, not in core. Core decides whether a person needs
to be asked; it never asks, because spec section 37 forbids a menu appearing in
a pipe or a CI job. Non-interactively the changes are left in place and
`loadout workspace save` is suggested.

## MCP servers

An agent loads MCP servers from several places at once — an account's
connectors, installed plugins, a project file, a user file — and nothing
reconciles them. Nobody sees the whole set until something behaves oddly.

`loadout mcp list` shows it, with where each server came from:

```text
serena              installed  uvx --from git+https://github.com/oraios/serena …
claude.ai Context7  installed  https://mcp.context7.com/mcp
context7            project    https://mcp.context7.com/mcp

claude.ai Context7, context7  the same service under more than one name, so every
tool it offers is loaded twice and the model sees each one twice
```

Servers the workspace declares are held there rather than in the repository, and
handed to the agent with `--mcp-config` at launch — so they are the same on
every machine that clones the workspace, instead of on whichever one happened to
have them configured.

Three things are reported:

| | |
|---|---|
| **The same service twice** | Two names reaching one endpoint. Every tool loads twice and the model sees each capability twice. |
| **A shadowed name** | One name declared in two places. One will not load, and which is not obvious. |
| **A machine-specific path** | A command or argument naming an absolute path. It cannot be right on another machine that clones the workspace. |

`loadout mcp add` runs the same check before writing and refuses a server that
clashes, unless you pass `--force`. Nothing is ever reconciled automatically:
which of two servers should win is a decision, and a launcher is the wrong place
to make it on somebody's behalf.

Only enabled plugins are counted. A plugin that is installed and switched off
contributes nothing, and warning about its servers would describe something that
is not happening.

### The launcher's own server

The handoff used to run one way: the launcher composed a context, started an
agent and heard nothing more. Every launch now also declares Loadout itself as
an MCP server, so a session can ask it things rather than parse console output
written for a person.

Five tools, each making the same call its command makes:

| | |
|---|---|
| `loadout_specialist` | The full text of one specialist, as `instructions show` prints it |
| `loadout_effective_instructions` | What this session was given, and what triggered each part |
| `loadout_recall` | Search what the project already knows, as `memory find` does |
| `loadout_remember` | Record one durable fact about the project, with a description, screened for credentials |
| `loadout_mode` | Change the posture for the rest of the session, and get what that changes |

`loadout_recall` exists because only the memory index reaches the context — one
line per topic — and a session deciding from that alone either opens six files
or opens none. It searches inside them. It matches words rather than meanings,
which it says when it finds nothing, so an agent doesn't conclude a fact is
unrecorded when it is recorded in other words.

`loadout_mode` is there because a mode is a session-wide directive and work
changes shape: a session that started out investigating a bug ends up fixing it.
The agent asks for the new posture and adopts it, rather than drifting into it
without saying so. A mode it doesn't recognise is refused with the list of real
ones instead of being treated as the default. `skill.mode-switch` tells it when
the switch is worth making.

Nothing offered pushes to a remote, changes the machine or starts an agent. A
tool an agent can call unprompted is a decision taken with nobody watching, so
what is offered is the part of the launcher where that is safe — and a test
asserts no tool is ever named for the rest.

The declaration is written into the launch's runtime directory rather than the
workspace, for the reason in the table above: it names the executable running
right now, and an absolute path in a shared file is right on the machine that
wrote it and wrong on every other one that clones the workspace. It goes when
the session does.

```bash
loadout config set agent-tools false
```

turns it off. What it offers is reading plus one screened fact, so it is on by
default — but preferring that an agent could not reach the workspace at all is a
legitimate position, and the launcher does not overrule it.

You would not normally run `loadout mcp serve` yourself: it speaks JSON-RPC on
stdin and stdout, and the agent starts it.
