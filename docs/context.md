# Context and instruction files

Each project gets a manifest in the central workspace at
`projects/<slug>/project.yaml`. It lists the instruction files that make up the
project's context, plus any named profiles:

```yaml
context:
  global:
    - global/instructions/engineering.md
  project:
    - context/architecture.md

profiles:
  database:
    description: Audit and schema work
    context:
      - context/database.md

environment:
  ANTHROPIC_API_KEY:
    secret: anthropic/default
```

At launch the compiler assembles those into one file, ordered from general to
specific — the specialists the task resolved to, then organisation policy, then
project, then the agent's own instructions, then the profile, then any handoff
— so where two sources disagree the agent reads the narrower one last. The file
lands in a per-launch runtime directory with owner-only permissions and is
deleted when the agent exits.

```mermaid
flowchart LR
    A["Specialists"] --> B["Global"] --> C["Project"] --> D["Agent"] --> E["Profile"] --> F["Handoff"] --> G["Rules"] --> H["Memory index"]
```

See what a launch would actually load, before you spend one:

```bash
loadout instructions explain --project starstats
loadout instructions explain --project starstats "why is this query slow"   --mode investigate
loadout rules budget starstats        # what loads regardless of the task
```

Specialists come first because they are the most general half of it: the Rust
specialist says what Rust should look like, and the project says how this
codebase departs from that. They are also the only part nobody writes per
project — which language, framework and database specialists apply is worked out
from the repository itself, so a project with three hundred `.rs` files gets the
Rust specialist without anybody saying so. See [specialists.md](specialists.md),
and `loadout instructions explain --project <slug>` to see what a launch would
actually load and why.

Two limits apply to everything in the list. A source file over 512KB is left out
and reported rather than truncated, because half a document is worse than a
clear note saying it was skipped. And a file named twice — by both the base
context and a profile, say — is included once.

The agent's own `agents/<agent>/instructions.md` is the one entry the compiler
adds itself rather than reading from the manifest, and the only one whose
absence is not reported: most projects have none, and warning about it every
launch would train people to ignore the warning that matters.

Only the launching agent's instructions are included, so a Claude session is
never handed Codex's notes. Adapters differ only in delivery: Claude gets the
file as a system prompt, Codex gets an ephemeral `CODEX_HOME` seeded from the
workspace with the compiled context as its `AGENTS.md`. The workspace clone is
never written to by an agent.

Secret references resolve through the platform keystore during preflight and
reach the child process only. The reference is what gets committed; the value
never is, and it is never written to a log or a diagnostic report.

## The agent can answer back

The compiled context tells the agent that `loadout` is on PATH, and names the
few commands worth running from inside a session: read a specialist in full,
ask what this session was given and why, record a fact worth having next time,
leave a handoff. Without that it was being told to change the source files in
the workspace without being told there was a tool for reaching it.

Anything that changes the machine or pushes to a remote is deliberately not
named, and the context says as much rather than leaving the omission to be
inferred.

The same three read-and-remember operations are also offered as MCP tools, which
every launch declares for itself — see
[the launcher's own server](commands.md#the-launchers-own-server). A session can
therefore call them rather than shell out and parse what comes back.

## Instructions that scale

An instruction file that is loaded on every session is paid for on every turn,
whatever the task. Two mechanisms keep that cost proportionate.

**Rules** are Markdown files under `global/rules/` and `projects/<slug>/rules/`
carrying frontmatter that says when they apply:

```markdown
---
description: Migrations and schema work
globs: src/Data/**, **/*.sql
alwaysApply: false
---
```

A rule with `alwaysApply: true` is inlined into every compiled context. A scoped
rule is not: the context lists its name, scope and path, and the agent reads it
when the work touches those paths. A project rule overrides a workspace rule of
the same name, so a project can depart from the house style without editing it.

```bash
loadout rules budget starstats     # what every session pays for
loadout rules audit starstats      # what costs tokens invisibly
loadout rules split starstats      # scope prose to the paths it applies to
```

`loadout rules budget` reports what loads regardless of the task.
`loadout rules audit` reports the defects that cost tokens invisibly — an
instruction written in two places, a rule that declares globs *and*
`alwaysApply` (the globs are decorative; it loads always), two rules claiming
the same paths, and `@import` lines whose size appears in nobody's budget.

`loadout rules split` breaks an existing instruction file apart. It needs a map
saying which sections belong to which rule and what each rule's scope is —
that judgement is about the project and the tool will not guess it — so start
with `loadout rules split --write-map`, fill in the globs, then preview:

```text
$ loadout rules split --from instructions.md
instructions.md  6.4KB today

  authentik   authentik/**, **/blueprints/**   1.5KB
    from  Authentik Blueprints (version 2025.12)
    from  Auth Patterns
  networking  **/docker-compose*.yml, traefik/**  932B
  secrets     **/.env*, secrets/**             1011B

  Always loaded  3KB  was 6.4KB
  On demand      3.4KB

Every line is accounted for.
```

Content moves verbatim, never reworded, and every non-blank line in the source
must appear at least as often across the outputs or the split is refused rather
than applied. A file that has already been split cannot be split again, because
the second pass would rebuild the rules from a source whose content has already
moved out of it.

