# Specialists: architecture finding

Written before implementation, from a survey of the existing code and of the
proposed `loadout-specialist-library` bundle.

## What Loadout already has

The launcher is further along than the bundle assumes. Six mechanisms already
exist that the proposal would otherwise have duplicated.

| Existing | Where | What it already does |
|---|---|---|
| Composition engine | `Context/ContextCompiler.cs` | Ordered plan (global → project → agent → profile → handoff), per-source provenance with byte counts, dedup by path, refuses oversized sources rather than truncating |
| Scoped instruction loading | `Instructions/RuleService.cs` | Frontmatter parsing, a hand-written cross-platform glob matcher, `Select(rules, paths)` |
| Instruction budget | `Models/Instructions/RuleDocument.cs` | `InstructionBudget` splits always-loaded from scoped bytes |
| Provider capabilities | `Models/Agents/AgentDescriptor.cs` | `AgentCapabilities` string keys, probed not assumed, `Supports(key)` |
| Provider neutrality | `Agents/IAgentAdapter.cs` | `AgentLaunchContext.CompiledContext` — "how it reaches the agent is the adapter's business; producing it is not" |
| Findings and diagnostics | `Diagnostics/IDiagnosticContributor.cs`, `RuleFinding` | Contributor seam feeding `loadout doctor` |

Two further invariants are already enforced rather than merely intended. The TUI
never executes behaviour: it emits a `LauncherIntent` carrying a command string
which `catalogue.RunAsync` parses exactly as the CLI would, and
`LauncherCommands` is tested to contain only real command names. And the command
palette already searches on `Intent` words, so discoverability needs content,
not a new engine.

`docs/context-budget.md` already states the layered price model — instructions
paid every session, scoped rules paid sometimes, memory paid as an index line.
**Specialists are a fourth layer in that same model**, not a new idea.

## What the bundle actually contains

77 markdown files, about 1,215 lines total, plus `registry.json`, a JSON schema
and four adapter notes. The prose is reasonable but thin, averaging under twenty
lines a file.

**The activation data does not exist.** All 52 specialists carry an identical
block:

```json
"activation": { "explicit": true, "projectPreference": true,
                "repositoryEvidence": true, "taskSemantic": true }
```

That records *which mechanisms are permitted* to activate a specialist. It does
not record *what evidence activates it*. There is not one glob, dependency
token or task phrase anywhere in the bundle. Every worked example in the brief —
`.cs` → C#, `Npgsql` → PostgreSQL, "optimise this query" → performance — has no
data behind it. The resolver is the substance of this feature, and the bundle
supplies none of its input.

Also absent: stable rule IDs (needed for the deduplication and conflict model),
dependency declarations (the brief asks for cycle detection over a graph nothing
declares), size or token metadata, and provider capability requirements.

## Decisions

**1. Extend `ContextCompiler`; do not build a second composer.** Specialists
become an ordered layer in `BuildPlan`, ahead of project context. Provenance,
dedup and the no-truncation rule are inherited rather than reimplemented.

**2. No hand-maintained registry.** The bundle keeps activation in
`registry.json` and prose in separate files. Deriving the library by scanning
for frontmatter instead removes three failure classes outright: registry/content
drift, "registry names a file that does not exist", and path traversal through
registry-supplied paths. A specialist becomes one self-describing file, authored
the way a rule already is.

**3. Built-ins are embedded resources; disk roots layer on top.** Ordered
`built-in → workspace → project`, later overriding earlier by id — the same
precedence `RuleService` already uses for rules. Adding a specialist needs
content, not a source change. Built-ins cannot be tampered with on disk or
escape a root, because they are not on disk.

**4. Kinds are a closed enum; specialists are data.** Composition order is
architecture and belongs in code. The library is content. An unknown kind must
fail, which an enum gives for free.

**5. Bytes stay authoritative, tokens are an estimate.** The existing budget is
in bytes and is exact. Tokens are reported alongside, derived and labelled as an
estimate, because no local tokeniser matches the providers'.

**6. Reuse `AgentCapabilities` for provider capability gating.** It is already
probed against the installed CLI rather than inferred from a version.

## What is deliberately not built

- A second glob matcher — `RuleService.Matches` is already cross-platform and tested.
- A second budget model — `InstructionBudget` is extended.
- A second findings subsystem — specialist problems become `RuleFinding` and `DiagnosticCheck`.
- A second palette — specialists are reached through commands carrying intent words.
- Multi-agent orchestration — out of scope by the brief, and the model is left able to express it later.
