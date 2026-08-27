# The context budget

Every agent session starts by reading something. What it reads, and how much of
it, is the one cost the launcher exists to control. This is the model.

## Three layers, three prices

| Layer | When it loads | What a line costs |
|---|---|---|
| Instructions | Every session, inlined in full | Its own length, every launch |
| Scoped rules | Only when the work touches their paths | Its own length, sometimes |
| Memory | Index inlined; topics fetched on demand | One index entry, then nothing |

The prices are what make the layers different, and they are not a matter of
taste. A fact in instructions is paid for on every launch whether or not the
session needed it. The same fact in memory costs one index line, and its body is
read only when something makes it relevant.

That difference is the whole reason for the tooling below. None of it deletes
anything: it moves content between layers whose prices differ.

## Reading the budget

```bash
loadout rules budget <project>
```

```text
  Always loaded  67.9KB  0 rule(s) plus core instructions
  On demand      0B  0 scoped rule(s)

Over the 20KB comfortable budget.
```

The 20 KB threshold is advisory and prompts a question rather than stopping a
launch. It exists because an instruction layer that has grown for a year is
rarely 20 KB of things every session needs; it is usually 20 KB of things some
session once needed.

`loadout drift` reports the same finding across every registered project, so an
oversized layer in a repository nobody has opened this month is still visible.

## Moving facts to memory

```bash
loadout memory compress <project>          # preview
loadout memory compress <project> --apply
```

Takes durable standing facts out of the always-loaded file and writes them as
memory topics grouped by the heading they sat under.

Three rules make it safe to run on a file somebody wrote by hand:

- **Verbatim, never reworded.** No model summarises anything, so the result
  cannot say something the source did not.
- **Read back before removal.** Nothing leaves the source until it has been read
  out of the memory store. A failed write costs nothing rather than losing the
  only copy.
- **List items only.** A bullet is a self-contained claim that can be lifted
  without leaving a hole. A paragraph usually is not, and pulling sentences out
  of prose is how an automatic tool turns a readable document into a confusing
  one.

Not everything moves. Lines that make no standing claim, that describe a change
the repository history already records, or that are dated to the moment they
were written are left where they are — memory that accumulates unfiltered costs
a session to read *and* misleads it, which is worse than having none.

### Credentials

Candidates are screened before they are grouped. The memory store screens too
and refuses a whole topic on one bad line, which is correct for a direct write
and wrong here: a single credential-shaped URL in a large file would otherwise
block every good fact in it and offer no way forward.

A withheld line stays exactly where it already was — disclosed no further than
it already was — and is reported by the name of the pattern that matched, never
by its content.

## Scoping the rest

What compression leaves behind is prose, which is scoped rather than moved:

```bash
loadout rules split <project> --write-map   # suggest
$EDITOR .../split-map.yaml                  # set the globs
loadout rules split <project>               # apply
```

The map arrives with globs already filled in wherever a heading names the paths
its section concerns — a heading reading ``Merit awards (`crates/core/src/recognition/store.rs`)``
has already said which files its rule is about. Backticked text that is not a
path is left alone: turning a type name or a flag into a glob would scope a rule
to files that do not exist, which is worse than leaving it unscoped, because it
would then silently never load.

A rule with no globs is refused. It would load always, which is the thing being
moved away from.

Content is moved verbatim here too, and the splitter proves it by counting:
every non-blank line in the source must appear at least as often across the
outputs, or the split is refused rather than applied.

## Order of operations

Compress first, then split. Compression takes the self-contained claims, which
are the cheapest thing to move and the most expensive thing to keep. Splitting
then deals with what is genuinely prose, and there is less of it to route.

Running them the other way works but scatters facts across rule files, where
each one is cheaper than before but still costs its own length whenever its
glob matches.
