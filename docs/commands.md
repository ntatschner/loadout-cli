# Commands

| Command | Purpose |
|---|---|
| `loadout` | Full-screen launcher, or first-run setup |
| `loadout setup` | Configure the launcher on this machine |
| `loadout <project>` | Launch the project's default agent |
| `loadout here` | Launch the agent for the current repository |
| `loadout doctor` | Platform, Git, workspace, secret and agent diagnostics |
| `loadout status` | Summary of workspace, projects and agents |
| `loadout project add\|list\|show\|remove\|discover\|open` | Manage project registration |
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
| `loadout repo check` | Check a repository for tracked AI tooling files |
| `loadout drift [project]` | Show where projects have drifted from their recorded configuration |
| `loadout drift --fix` | Put right the drift the launcher can fix itself |
| `loadout doctor --fix` | Put right the findings the doctor can fix itself |
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
| `loadout memory compress <project>` | Move durable facts out of always-loaded instructions into memory |
| `loadout sessions` | List recent agent sessions across every agent, newest first |
| `loadout resume [session]` | Reopen a previous session, with a picker when none is named |
| `loadout instructions list\|show\|explain\|validate` | Inspect the specialists an agent is given, and why each one is there |
| `loadout instructions new <id>` | Draft a specialist or skill in the workspace, or in one project |
| `loadout usage [--days\|--by\|--project]` | What the agents have spent, by project, day, model or agent |
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

## Saving what a session produced

Agents change workspace files during a session — context notes, decisions,
handoffs. When the agent exits, the launcher applies the exit policy from
`config.yaml`:

| `sync-exit` | Behaviour |
|---|---|
| `prompt` (default) | Offers save-and-sync, save-locally, review, or leave |
| `always` | Commits and pushes without asking |
| `never` | Leaves the changes alone |

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

