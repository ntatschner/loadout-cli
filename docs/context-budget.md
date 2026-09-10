# The context budget

Every agent session starts by reading something. What it reads, and how much of
it, is the one cost the launcher exists to control. This is the model.

## Three layers, three prices

| Layer | When it loads | What a line costs |
|---|---|---|
| Instructions | Every session, inlined in full | Its own length, every launch |
| Scoped rules | Only when the work touches their paths | Its own length, sometimes |
| Memory | Index inlined; topics fetched on demand | One index entry, then nothing |
| Code map | Every session, only where the project asks for it | One line per directory, every launch |

The prices are what make the layers different, and they aren't a matter of
taste. A fact in instructions gets paid for on every launch, whether or not the
session needed it. The same fact in memory costs one index line, and its body is
only read when something makes it relevant.

That difference is the whole reason for the tooling below. None of it deletes
anything. It moves content between layers whose prices differ.

Two layers stay off until a project asks for them, and `loadout project context`
is where you read and change both:

```bash
loadout project context             # what this project carries, and what not
loadout project context tasks on    # takes effect at the next launch
```

The code map is the bigger one: `loadout project context code-map on`, which is
`code_map: true` under `context` in the manifest. It's the digest half of the
machine index — one line per directory naming the types it holds — inlined so a
session can pick where to look without reading the tree. On a mid-sized
repository that's a few thousand tokens every launch, roughly what everything
else in the context costs put together. Worth it for a session that explores
widely, wasted on a one-line fix. The lookup behind `loadout docs find` works
whether or not the map is on, and costs nothing until you ask it something.
`loadout instructions explain` shows the map's price on its own line, sitting at
zero until you switch it on, so you can decide with the figure in front of you.

The other is open tasks: `loadout project context tasks on`, written as `tasks:
true` under `context`. It inlines what the project is working on — the open,
doing and blocked entries from `loadout task list`, each with who said so and
when — so a session can pick up where the last one stopped instead of asking.
It's cheap, a line or two per task. It's off by default for a different reason
from the map: the record is a claim somebody made rather than a fact about the
code, and a project that doesn't keep one would otherwise pay for a heading
saying so on every launch.

Finished and dropped tasks stay in the record and out of the context, because
nobody has to act on them. A project registered before it had a repository comes
with this switched on, since the setup task is the whole reason it was
registered that way. Every other project starts with it off, so `loadout task
declare` tells you when it's recording into a project that nothing will read it
back from.

## Reading the budget

```bash
loadout rules budget <project>
```

```text
  Always loaded  67.9KB  0 rule(s) plus core instructions
  On demand      0B  0 scoped rule(s)

Over the 20KB comfortable budget.
```

`loadout instructions explain` adds the three layers together in one unit, so
there's a single figure for what a session costs before it starts:

```text
Context
  Specialists                2,403
  Instructions and rules         0
  Memory index                 474
  Every launch               2,877

  Budget (specialists)      12,000
  Usage                        20%
```

The ceiling is named for what it governs. It's enforced against the specialist
layer and nothing else, because that's the one the resolver can negotiate down.
Calling it the budget for the whole context would be a promise the launcher
doesn't keep. Scoped rules are counted apart from the rest, since the whole
reason the layers exist is that their prices differ, and a total that added them
up would hide it.

The 20 KB threshold is advisory. It asks a question rather than stopping a
launch. It's there because an instruction layer that's grown for a year is
rarely 20 KB of things every session needs — it's usually 20 KB of things some
session once needed.

`loadout drift` reports the same finding across every registered project, so an
oversized layer in a repository nobody has opened this month still shows up.

## Moving facts to memory

```bash
loadout memory compress <project>          # preview
loadout memory compress <project> --apply
```

This takes durable standing facts out of the always-loaded file and writes them
as memory topics, grouped by the heading they sat under.

Three rules make it safe to run on a file somebody wrote by hand:

- **Verbatim, never reworded.** No model summarises anything, so the result
  can't say something the source didn't.
- **Read back before removal.** Nothing leaves the source until it's been read
  out of the memory store again. A failed write costs you nothing instead of
  losing the only copy.
- **List items only.** A bullet is a self-contained claim you can lift without
  leaving a hole. A paragraph usually isn't, and pulling sentences out of prose
  is how an automatic tool turns a readable document into a confusing one.

Not everything moves. Lines that make no standing claim, that describe a change
the repository history already records, or that are dated to the moment they
were written all stay put. Memory that piles up unfiltered costs a session to
read *and* misleads it, which is worse than having none.

### Credentials

Candidates get screened before they're grouped. The memory store screens too,
and refuses a whole topic over one bad line. That's right for a direct write and
wrong here: a single credential-shaped URL in a large file would block every
good fact in it and offer you no way forward.

A withheld line stays exactly where it was, disclosed no further than it already
was, and gets reported by the name of the pattern that matched. Never by its
content.

## Scoping the rest

What compression leaves behind is prose, and prose gets scoped rather than
moved:

```bash
loadout rules split <project> --write-map   # suggest
$EDITOR .../split-map.yaml                  # set the globs
loadout rules split <project>               # apply
```

The map arrives with globs already filled in wherever a heading names the paths
its section is about — a heading reading ``Merit awards
(`crates/core/src/recognition/store.rs`)`` has already told you which files its
rule concerns. Backticked text that isn't a path is left alone. Turning a type
name or a flag into a glob would scope a rule to files that don't exist, which
is worse than leaving it unscoped, because then it silently never loads.

A rule with no globs is refused. It would load always, which is the thing you're
moving away from.

Content moves verbatim here too, and the splitter proves it by counting: every
non-blank line in the source has to appear at least as often across the outputs,
or the split is refused rather than applied.

## Order of operations

Compress first, then split. Compression takes the self-contained claims, which
are the cheapest thing to move and the most expensive thing to keep. Splitting
then handles what's genuinely prose, and there's less of it left to route.

The other way round works, but it scatters facts across rule files, where each
one is cheaper than before and still costs its own length every time its glob
matches.
