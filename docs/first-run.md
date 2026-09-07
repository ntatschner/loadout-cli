# First run and configuration

```bash
loadout setup
```

Running `loadout` with no arguments on an unconfigured machine goes here too,
because an empty project list tells a new user nothing about what to do next.

Every question can also be answered up front, so provisioning a machine needs no
one sitting at it:

```bash
loadout setup --create-new --github --name agent-workspaces   --register-discovered --migrate --global-excludes --non-interactive
```

Both routes run the same code — an interactive run is just one where nothing was
answered in advance — so the scripted path cannot drift from the one people see.
Anything genuinely unanswerable stops before doing any work and names the flag
that would settle it, rather than failing halfway through a setup.

If you choose to create a new workspace and the GitHub CLI is installed and
signed in, it offers to create the private repository and push for you. That is
a convenience for one common host, not a dependency: the launcher is
provider-agnostic (spec section 10), the other option takes any Git URL, and
Forgejo, GitLab, Azure DevOps or a bare SSH repository all work the same way.
The repository is always created private — a workspace holds project context,
decisions and handoffs, and making that public is an irreversible disclosure
that should not be one keystroke away.

The wizard offers the three choices of spec section 61 as equals — point at an
existing central workspace, create a new one, or **run without central
storage**. The last is a real way to use the tool, not a degraded mode: it
creates the same directory layout locally, so adopting a shared workspace later
is a matter of pushing what you already have.

It then checks Git is present before asking anything and sets a **global** Git
identity if none exists — global specifically, because a plain config read
resolves through whatever repository you happen to be standing in, and a local
identity in an unrelated project must not be mistaken for one the workspace can
use. Without it every workspace commit fails with "Author identity unknown".

It picks a secret provider that actually works on this machine, lists the
repositories it found in your development roots, offers to register them, and
then offers to migrate any agent files out of them.

Migration runs **before** the global Git excludes are installed, and the order
matters: installing the excludes first would make the very files migration
exists to move become ignored, so setup would protect the repository and then
report nothing to migrate. Clean up first, then stop it happening again.

## Adding an agent nobody compiled in

An agent under `custom_agents` needs an executable, arguments and environment to
launch. To also appear in `loadout sessions` it has to say where it writes its
transcripts:

```yaml
custom_agents:
  scribe:
    display_name: Scribe
    executable: scribe
    arguments: ["--context", "${COMPILED_CONTEXT_FILE}"]
    transcripts:
      root: "~/.scribe/sessions"
      files: "*.jsonl"
      recursive: true
      session:
        id: "sessionId"
        directory: "cwd"
        title: "meta.title"      # optional
        first_line_only: false
      usage:                     # optional; without it the agent is listed but not counted
        timestamp: "timestamp"
        directory: "cwd"
        model: "message.model"
        id: "message.id"
        input: "message.usage.input_tokens"
        output: "message.usage.output_tokens"
        cache_read: "message.usage.cache_read_input_tokens"
        cache_write_5m: "message.usage.cache_creation.ephemeral_5m_input_tokens"
        cache_write_1h: "message.usage.cache_creation.ephemeral_1h_input_tokens"
```

Paths are dotted and name properties inside the JSON object on one line. That's
the whole language: every transcript format seen so far puts what's wanted at a
fixed place, and a query language nobody asked for is one that has to be
documented, tested and kept.

The field names above are an example of the *shape*, not a description of any
real agent. Nothing ships describing an agent's format on its behalf, because a
guess at somebody else's undocumented file would be wrong in a way that looks
right. To write your own: find a transcript, look at one line of it, and name
the properties holding the session's identifier and its working directory.

`first_line_only` matters more than it looks. Codex opens each rollout with a
metadata entry, so reading stops after one line; other agents repeat the working
directory throughout, so it has to read until it has what it needs. Reading a
whole conversation to put a name in a menu is the difference between a listing
that's instant and one that isn't.

A described agent taking the name of a built-in one **replaces** it. That's the
point rather than an accident: these formats aren't published and change without
notice, so when one breaks you can correct it here the same afternoon instead of
waiting for a release.

`id` under `usage` is worth setting even though it's optional. Agents copy
earlier accounting into the transcript of a resumed conversation, and without
something to tell one record from another there's no way to see a repeat, so
they're all counted. That's the easiest way to produce a number that's wrong and
looks right.

Two limits, said rather than discovered. A title kept in a separate index file —
as Codex does — can't be expressed, because there's no way to say "join these two
files on an identifier"; those sessions list by directory instead. And there's
one path per field with no alternatives: Claude's own reader has a fallback for a
cache figure that's sometimes a nested object and sometimes a flat number, and
that can't be said here. An agent whose format needs one has earned a reader
written by hand.

What the description misses is reported rather than absorbed. A record carrying
an identifier but no number these paths can find is counted as unrecognised, and
`loadout usage` says the totals are incomplete — because a reader that meets a
renamed field doesn't fail, it counts zero and returns a total that looks
entirely reasonable.

## Exporting documentation

```
loadout docs export --type reference     --out docs/reference.md
loadout docs export --type technical     --out docs/architecture.md
loadout docs export --type machine-index --out docs/index.txt
loadout docs export --type user-guide    --out docs/guide.md
```

The language of each file comes from its extension, and the scan reads C#,
TypeScript and JavaScript, Python, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C
and C++, PowerShell, shell, Terraform and SQL. It is lexical — a pair of line
patterns per language and the comment style that documents a declaration — so
where it is wrong it leaves something out rather than inventing it. A file in a
language it doesn't know is skipped, not guessed at.

**The four are not equally derivable, and the output says which is which.** The
reference and machine index fall out of the code — always true, always dull,
never need a person. The technical guide is the prose already sitting in your
doc comments, arranged by module. The user guide is barely derivable at all,
because what somebody wants to *do* isn't in the source.

So the user guide is emitted as a **scaffold that says it is one**, and the
command says so again on the way out. Generating it from symbols would produce
something that reads like documentation, teaches nobody anything, and — worst of
the three — looks finished enough that nobody writes the real thing.

The **technical guide** carries the decisions, not just the summaries: under each
type it prints the opening paragraph of its `<remarks>`, which is where this
codebase puts the reasoning. Only the opening paragraph — what follows is the
evidence and the history, and that belongs where somebody changing the code will
meet it rather than in a guide read end to end.

The **machine index** opens with a digest of the modules and what each holds, so
a session can pick a file to open instead of reading the tree, and follows it
with one tab-separated line per symbol.

### Which files, and which languages

The files come from git: everything tracked, plus anything present that
`.gitignore` does not exclude. A hand-kept list of directories to skip is
always one short — it had left this repository's own scripts out, because they
live under `build`, and let a parked virtual environment in, because it lived
under a name nobody had thought of. The project has already written down what
is its own, and git applies it exactly. Outside a repository the tree is walked
with the old rules.

Where [Universal Ctags](https://ctags.io) is on `PATH`, it reads the files the
built-in table does not — well over a hundred languages — and the two halves
are joined by file, so nothing is counted twice. The table keeps the languages
it knows, because it also reads the comment that documents a declaration and
ctags does not. A machine without ctags gets the table and no message about a
tool it never had.

A project can say more in its manifest, under `symbols`:

```yaml
symbols:
  ignore:
    - generated/**
  extensions:
    .pyw: python
  languages:
    - id: elixir
      name: Elixir
      extensions: [ex, exs]
      types: '^\s*defmodule\s+(?<name>[\w.]+)'
      members: '^\s*def(?:p)?\s+(?<name>\w+)'
      docs: hash
```

`ignore` takes globs the scan leaves out even though git lists them.
`extensions` maps an extension onto a language the table knows. `languages`
describes one the table does not: a pattern for a line declaring a type
(optional) and one for a function or member, each with a `name` group, and how
the language documents a declaration — `hash`, `double_slash`, `slashes`,
`double_dash`, `block` or `docstring_below`. Run `loadout docs find` once after
writing one: a pattern that does not compile drops its language rather than
failing every lookup.

### Finding one thing

```
loadout docs find PreflightService
```

The same scan, kept rather than written out. `docs find` says where a type or
member is declared, as file and line, and is what the compiled context points
an agent at when it knows a name — one line back instead of
a search across the tree and whatever it opened on the way. An agent launched
with the launcher's own tools gets it as `loadout_locate`, through the same
code, so the two cannot drift.

The index is cached under the machine's cache directory against the commit it
was built at, and thrown away when the commit moves. That is a coarse key, so
two rules keep the answer right about the tree as it stands: every file a cached
hit names is read again before it is reported, and a cached miss is checked
against a fresh scan before it is reported as one. The cache can make a hit
faster; it cannot make an answer wrong. `--rescan` reads the tree regardless.

The commit is a coarse key, and a session's edits do not move it. A lookup
copes: it re-reads any file it names, and a name the index has never seen sends
it back to the tree. `loadout docs refresh <file>` does better for a file that
has just changed, replacing that file's entries in the index at the cost of
reading one file, so a name added a minute ago is found without a rescan and
the directory map is corrected as the edits happen. It is made to be run by an
agent's after-edit hook; with no index yet it builds one, so the index is warm
by the time the agent asks.

`loadout protect --refresh-hook` installs that hook, in the project's own
Claude settings file in the workspace rather than the user's, since it
refreshes one project's index. It writes the launcher by name, never by path:
the file syncs between machines, and the launcher substitutes its own location
when it hands the file to Claude.

That hand-over is now screened, by the rule pre-approvals already follow. A
file that travels between people and machines may only tighten, and a hook is a
command run after every edit. So the hooks in the project's settings file are
read at launch: the launcher's own is kept and pointed at this machine's
launcher, anything named under `commands.allowed_hooks.<slug>` in `config.yaml`
— which stays on this machine — is kept as written, and the rest is dropped and
named in a warning that says how to allow it here. An entry is the whole
command or a prefix of it ending at a word, so `prettier` allows
`prettier --write`. Everything else in the file passes as it always did, and a
file with no hooks is handed over untouched. The screened copy lives in the
launch's own runtime directory and goes when the session does.

```yaml
# config.yaml, this machine only
commands:
  allowed_hooks:
    starstats:
      - prettier
      - npm test
``` In hook mode the command reads the edited file
from what Claude sends it and says nothing back unless a directory's line on
the map changed — a type added, removed or renamed. An edit inside a method,
which is most of them, passes in silence. That one line is the only way a
change made during a session reaches a running agent: the compiled context is
read once at launch, so the map in it cannot be rewritten in place, but a line
added to the conversation can correct it. The file is in the workspace, so the
hook travels with the next `loadout workspace save`. Codex has no such hook,
and there the lookup's own re-reading is the whole answer.

### From other agents and editors

Everything above is served over MCP as well as on the command line, and the
server needs nothing but a registered project: `loadout mcp serve --project
<slug>` in any MCP client's configuration — Cursor, VS Code, Codex — gives that
client `loadout_locate` and `loadout_code_map`. The map tool returns the digest
as it stands now, pulled rather than pushed, which is what an agent with no
after-edit hook has instead of the hook. The hook command itself reads the
payloads agents actually send — Claude's `tool_input.file_path`, Cursor's
top-level `file_path`, or a plain `files` list — and `--dialect generic` makes
it write plain text for a hook that shows or ignores stdout rather than the
document Claude reads back. Only the Claude entry is installed by
`loadout protect --refresh-hook`; a Cursor hook lives in a file the repository
policy keeps out of the repository, so that one is yours to write.

A project that wants the map itself in every session, rather than one lookup
at a time, sets `code_map: true` under `context` in its manifest. That inlines
one line per directory naming the types it holds, at a cost of a few thousand
tokens on every launch; [the context budget](context-budget.md) says when that
is worth paying, and `loadout instructions explain` shows the figure.

It is not memory, and deliberately so. Memory holds what the code does not say
and travels with the workspace; a symbol index is derived from one checkout at
one commit and would fail `memory audit` on the day it was written.

### Publishing it

`--front-matter` adds the YAML header Docusaurus and MkDocs read, so the files
drop into either unchanged. Both consume plain Markdown otherwise, so there is
no conversion step and no new dependency.

`loadout docs ci` writes a GitHub Actions workflow that regenerates the
documents. It says in its own first line that it is a starting point, and it
means it: action versions move and runner images change, and neither is
Loadout's to keep up with. It also assumes Loadout is on `PATH` and the project
registered, and it **writes nothing back** — no commit, no pull request, no
publish. Each of those writes somewhere, and where is a decision about your
repository rather than a default worth guessing.

**The user guide is excluded from the pipeline by default.** It is a scaffold,
and a pipeline that regenerated and published it nightly would undo the entire
reason for marking it as one. `--include-user-guide` overrides that, once you
have read it.

`llms.txt` never gets front matter, even when the flag is on. It is read by a
model rather than rendered by a site, and a YAML preamble is noise in the one
file whose purpose is to say where things are in as few tokens as possible.

For CI this cannot write, `skill.publish-documentation` covers adapting the
commands to whatever a repository already uses, and what to check before wiring
any of it up.

The scan is **lexical, not a parse**. It reads declarations the way you would
skimming, which gets the overwhelming majority right and will miss a declaration
split across lines. The alternative is Roslyn: a large dependency for a
launcher, to produce a document nobody compiles. Where the scan is wrong it
omits rather than invents, which is the failure worth having — so the reference
calls itself an index rather than an authority.

## Tasks and the backlog

```
loadout task declare add-the-widget doing --title "the widget nobody has added"
loadout task list
loadout task list --all
loadout task remove add-the-widget
```

Kept apart from memory because the two answer different questions. A memory is
something that **stays** true — how this machine behaves, what broke last time.
A task is true today and stops being true. Mixing them fills the durable store
with things that expire.

Every state carries who said it and when, which is what makes it checkable.
`task list` then asks the repository whether the record backs the claim up, and
reports what it doesn't:

```
What the record does not back up
  probe-b called done, and nothing has been committed since it was said.
          That may be right - work does not always leave a commit.
```

**These are observations, not verdicts.** Corroboration can say a claim is
unsupported; it can never say a claim is wrong. Two consequences follow, and
both are deliberate:

- Nothing is matched on commit messages. "Committed under a message that never
  named the item" is the overwhelmingly common case, not a problem — flagging it
  would make the report mostly noise, and a report that's mostly noise stops
  being read.
- A repository that can't be read reports **nothing** rather than an empty
  history. With no commits, "nothing committed since" would fire on every task
  at once — a confidently wrong answer where none was needed.

Agents get the same thing over MCP: `loadout_tasks` answers "where were we" from
the record, carrying the disagreements with it so a session is told *"you said
this was done and nothing was committed"* rather than being handed its own claim
back as fact. `loadout_task_declare` records a state and nothing else — it never
launches, pushes or changes the machine, and what it writes is screened for
credentials the way memory already is. That screening lives in the store, so
every caller gets it rather than the one somebody remembered.

## Suggested replies

`task list` ends with a few short replies you can accept instead of composing:

```
Next  composed from the record above
  continue widget
  why is parser blocked
  start docs
```

Offered in the order you'd act in — underway, then stuck, then what the record
doesn't back up, then not started. A list opening with the untouched backlog
would be answering a question nobody asked mid-session.

**Composed and drafted are never blended, and that is the whole safety of it.**
A composed reply can't be wrong about the state it names, because it was
assembled *out of* that state. A reply an agent drafts can be confidently wrong
about exactly the same thing, in exactly the same shape. The only defence anyone
has is being told which they're looking at, and merging the two lists for
tidiness would take that away.

Over MCP the composed ones arrive labelled, with the session told plainly that
anything further is its own draft and should say so.

Note the wording: **check**, never **fix**. The record not backing a claim up
isn't the same as the claim being wrong, and a suggestion saying "fix" would
settle that question on nobody's authority.

Nothing is ever taken automatically. Offering an action and performing it are
different features, and only the first one is here.

## What is running now

```
loadout running
loadout running --idle-after 15
loadout running --json
```

Each line is a session the launcher started, how long it's been going, and
whether it's said anything lately. Quiet times come from each agent's own
transcript — its last write — joined to the registry on the directory the
session runs in, because that's the one thing both sides record.

**It is passive, and that is the point.** Nothing here attaches to a console,
reads another process, or drives a terminal. Doing that on this machine once
took out every live session on it, and a monitor that can break what it watches
isn't one worth having.

A session whose transcript can't be found reads as **unseen**, not idle. Neither
agent publishes its transcript format, so a session this can't see is one it
can't judge — and "idle" would tell you your agent had stopped when it may be
working perfectly well. Idle is a description and never a verdict: nothing is
stopped, and a session quiet for an hour is working again the moment it writes.

Not built: the desktop notification when a session goes idle or ends. That needs
a notification seam on three platforms and can't be verified headlessly — the
kind of thing that ships looking finished and isn't. The reading half is here
and honest; the notification is worth doing deliberately rather than as a
footnote.

## Checkpoints

A checkpoint is a named marker binding four things that were already there
separately: the project's workspace files as they were, the commit the
repository was on, the handoff current at the time, and the session it was taken
during. Nothing here is new except the record that they belong together.

```
loadout checkpoint create before-the-refactor --because "works, before I break it"
loadout checkpoint list
loadout checkpoint restore before-the-refactor          # previews
loadout checkpoint restore before-the-refactor --apply  # writes
loadout checkpoint remove before-the-refactor
```

**It never moves your repository.** Restoring puts the workspace files back and
*tells you* the commit — checking one out can discard work nobody asked to lose,
and doing that because you typed a checkpoint name is exactly the surprise
preview-before-mutation exists to prevent. Running `git checkout` is yours.

A checkpoint taken on a dirty tree says so, at the time and again on the way
back: the commit it recorded doesn't describe everything that was on disk.

Creating one never overwrites an existing name. A checkpoint exists to be
returned to, and quietly replacing one is the single way this could destroy the
thing it was built to protect — `remove` first if that's what you meant.

## Spend thresholds

Thresholds tell you where you stand. They stop nothing, and that isn't a
limitation to be fixed later — Loadout starts an agent and is then out of the
loop, so a limit enforced at the door would be crossed by the very session it
let in and nothing here would see it. Refusing to launch was considered and
declined: a threshold that blocks work is one you set high enough never to fire.

```yaml
spend:
  daily_tokens: 20000000
  project_daily_tokens:
    loadout-cli: 5000000
  plan_warn_at: 0.8
```

`daily_tokens` is everything today, `project_daily_tokens` is one project today,
and `plan_warn_at` is the share of a plan's rate window — on a subscription that
is the number that actually constrains the work, because money isn't what runs
out, the window is.

**Nothing is read unless something is set.** Working out what's been spent means
reading the agents' transcripts, measured at about two seconds on this machine,
and that isn't a cost to put on everybody who never asked for a threshold. Zero
means off rather than a limit the first token of the day crosses.

Only Codex writes its standing in the rate window to disk, and only sometimes,
so a reading may simply not be there. That's reported as no answer, never as
plenty of room left, and it always carries how old it is — an hours-old
percentage shown as a live gauge is worse than no gauge.

It does appear in the status line, without ever scanning there. The answer is
written down whenever something works it out — at launch, or by
`loadout spend refresh` — and the line reads that file in microseconds. When the
figure goes stale the line starts a refresh **detached** and draws immediately;
the number catches up a moment later rather than holding up the prompt.

Exactly one caller gets to start that refresh. The line is redrawn several times
a minute, and without a claim every one of those would see the same stale file
and launch its own two-second scan.

`loadout spend refresh` is worth running by hand after changing a threshold —
otherwise you'd wait a quarter of an hour to find out whether you're over it.

The status line shows the composed specialist count the same way — `12 spec` or
`12 spec/review` — read from what the launch wrote down. Resolving the library
takes about half a second, and half a second per keystroke is not a status line.
A session started outside the launcher has nothing written, so the segment is
absent rather than claiming zero.

## Sharing what belongs to everybody

```
loadout share candidates
loadout share promote projects/demo/specialists/style.md          # previews
loadout share promote projects/demo/specialists/style.md --apply  # moves it
```

`share candidates` looks for guidance filed under a project that never mentions
that project — often something general somebody put in the nearest folder. It's
a **weak signal, stated as one**: the reason is printed with every candidate so
you can dismiss it at a glance. Nothing is moved, and nothing is decided.

It exists because "publish deliberately" becomes "publish never" if nobody is
ever prompted. A rule that depends on remembering is a rule that decays.

**The private half of a workspace is never searched.** Handoffs, memory and
state are why a workspace is created private — publishing them is an
irreversible disclosure. Those directories are excluded twice over: the search
uses an allow list rather than a deny list, *and* they are refused by name if you
type one directly. Widening one must not quietly widen the other.

`share promote` previews by default, scans for credentials before anything
moves, and refuses on a finding — naming the pattern, never the value. It
**writes locally and never pushes**: `loadout workspace save` is what shares it,
and that scans again on the way out.

## Specialist packs

House standards fetched from a Git remote, resolving alongside the built-ins:

```
loadout pack add house https://example.com/house-standards.git
loadout pack list
loadout pack approve house      # after reading it
loadout pack update house       # moves the pin, and costs the approval
loadout pack remove house
```

**Fetching is not approving, and that split is the whole feature.** A pack's
content becomes instructions an agent follows, and the declaration lives in a
workspace anybody on your team can edit. So the declaration *proposes* and your
machine *decides* — the same rule command policy uses, guarding the same
failure: a change reaching your machine because it reached somebody else's
repository.

**Approval is of a commit, never of a pack.** Approving "the standards pack"
would mean approving whatever it says next week. Move the pin and it stops
loading until somebody reads the change and approves again — that is arithmetic
in the gate, not bookkeeping anyone can forget.

A pack pinned to no commit loads nothing at all. It would otherwise load
whatever its branch says today, which is the unpinned dependency this refuses.

Packs layer **over the built-ins and under the workspace**. A pack is standards
from elsewhere; your workspace and your project are yours, so whatever they say
wins — adopting a pack must not quietly overrule a decision somebody made
deliberately. `loadout instructions show` says `pack` for anything that came
from one.

The approvals live on this machine and are never committed. Nobody can take
responsibility for what your agent is told on your behalf.

## Onboarding defaults

Registering a project asks the questions it currently makes you answer later —
usually after the third time something surprises you:

```
loadout config set onboarding-agent codex
loadout config set onboarding-model big-model
loadout config set onboarding-models "review=small-model;implement=big-model"
loadout config set onboarding-editor Agents
```

`loadout project add` then fills those in and **says what it filled**:

```
Registered Demo (demo)
  agent: codex (from your defaults)
  model for review: small-model (from your defaults)
```

**Blanks only.** A project that names its own agent chose that, and a
machine-wide preference is not grounds to reconsider it. The one exception is
`claude` as the agent: that's the built-in default rather than a choice anybody
made, so a configured preference replaces it.

Filling is per setting, not per section — a project that pins a model for
`review` but not `implement` gets `implement` filled in and `review` left alone.

**Two things are deliberately absent.** Nothing that reaches off this machine
has a default: the rule everywhere else here is that outward-facing things are
confirmed rather than switched on for you, and a default is the opposite of
confirming. And remote control isn't here at all — it doesn't exist in Loadout
yet, so a setting to enable it automatically would be a setting for nothing.

## Pinning a model

Loadout never chose a model, so the choice was retyped after `--` every session
or, more often, forgotten. A project can pin one, and pin a different one per
mode:

```yaml
agents:
  default: claude
  model: big-model
  model_by_mode:
    review: small-model
    advise: small-model
```

Names are written the way the agent spells them. Loadout translates the *flag*,
not the name — there's no shared vocabulary of models across agents, and
inventing one would mean maintaining a mapping that's wrong the week either of
them ships something new.

The mode's entry wins over the project's; a project with no `model` at all
leaves the agent on its own default, which is the common case. A build that
doesn't advertise a model option is told about rather than quietly started on
something else. And a model you still type after `--` wins over both: the
manifest ends the retyping, it doesn't take the choice away.

Nothing here infers anything. Choosing a model from how hard the work looks
would mean reading difficulty out of token counts, which is a guess wearing a
metric's clothes.

`loadout launches` breaks launches down by posture, with the context size each
was given. That is **not** spend, and it is deliberately not in `loadout usage`:
what the agents record is per day, per directory and per model, so a day in
which you reviewed and then implemented can't be split between the two. A mode
column in a spend report would be a number you'd act on and nothing could
support. For spend by model, `loadout usage --by model` already answers that.

## Adding an editor nobody compiled in

Naming a different editor was always possible with `editor-command`. What it
couldn't say is how that editor takes a **profile**, and that's the part worth
having — it's what lets opening a project for Claude and for Codex give you
different extensions and settings. Editors differ in kind here, not in spelling:

```yaml
custom_editors:
  helix:
    executable: hx
    arguments: ["${DIRECTORY}"]
    terminal: true
    profile_environment: HELIX_RUNTIME
```

`${DIRECTORY}` is the folder being opened and `${PROFILE}` the profile chosen
for it; both expand in arguments and in environment values, and an unset profile
expands to nothing rather than to the literal text.

A profile reaches the editor one of two ways. `profile_arguments` are added to
the command line only when a profile was chosen, and `profile_environment` names
a variable to set instead. Neovim is recognised by name and uses the second:
`NVIM_APPNAME` names the configuration directory it loads, so a profile is a
directory beside your `nvim` one and switching is nothing more than starting the
editor.

`terminal: true` says the editor draws on the terminal it was started from, so
Loadout waits for it. A windowed editor is let go instead, because it outlives
the launcher and there's no exit code worth having.

The VS Code family is recognised by name and deliberately declares **no**
`profile_arguments`. Asked for a folder and a profile together it opens a window
containing neither and reports nothing; asked for the folder alone it opens every
time. `loadout code` says the profile wasn't used rather than leaving you to
find out. As with agents, a described editor taking the name of a built-in one
replaces it — so if that's ever fixed, you can say so without waiting for us.

An editor nothing knows about is never reported as having ignored a profile.
"I can't check" and "it isn't there" are different answers, and only one of them
sends somebody looking for a problem they don't have.

## Environments and security profiles

A project can define environments, and selecting one changes both which
credentials resolve and how much the agent is allowed to do:

```yaml
environments:
  production:
    description: Production investigation
    security_profile: production
    environment:
      DATABASE_URL:
        secret: starstats/production-db
```

```bash
loadout starstats --environment production
```

Security profiles are expressed in the launcher's own vocabulary — filesystem,
network, approvals, tool lists — and each adapter translates them into whatever
its agent actually supports. A project says "production work is read-only"
once, and Claude and Codex each honour it as far as they can:

| Profile filesystem | Claude | Codex |
|---|---|---|
| `Repository` | agent default | `--sandbox workspace-write` |
| `ReadOnly` | `--permission-mode plan` | `--sandbox read-only` |
| `Restricted` | `--permission-mode manual` | `--sandbox read-only` |

**A profile can only ever tighten.** There is no value that loosens an agent's
defaults, and the adapters never emit `--dangerously-skip-permissions`,
`bypassPermissions`, `danger-full-access` or their equivalents. A profile lives
in a shared repository; if one could loosen a sandbox, anyone who could edit
that repository could switch off somebody else's safety controls. Tests assert
this over every built-in profile.

Naming an environment that does not exist stops the launch rather than falling
back — someone who typed `--environment prod` meaning `production` must not
quietly get development's permissions.

Where an installed agent does not advertise the option needed to enforce part of
a profile, the launcher says so instead of proceeding silently.

