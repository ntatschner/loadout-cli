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
specific — organisation policy, then project, then the agent's own
instructions, then the profile, then any handoff — so where two sources
disagree the agent reads the narrower one last. The file lands in a per-launch
runtime directory with owner-only permissions and is deleted when the agent
exits.

Only the launching agent's instructions are included, so a Claude session is
never handed Codex's notes. Adapters differ only in delivery: Claude gets the
file as a system prompt, Codex gets an ephemeral `CODEX_HOME` seeded from the
workspace with the compiled context as its `AGENTS.md`. The workspace clone is
never written to by an agent.

Secret references resolve through the platform keystore during preflight and
reach the child process only. The reference is what gets committed; the value
never is, and it is never written to a log or a diagnostic report.

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

