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

One thing this deliberately doesn't do: appear in the status line. That runs on
every prompt, and a two-second transcript scan there would be felt on every
keystroke. Warnings show at launch, where the cost is paid once. Putting it in
the status line would mean caching the answer at launch and labelling it as of
session start, which is a different feature from the one asked for.

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

