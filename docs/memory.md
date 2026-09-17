# Memory

Memory holds the durable facts a session shouldn't have to rediscover:
architecture, decisions and why they were made, non-obvious build behaviour,
traps that keep catching people. It lives in the workspace repository at
`projects/<slug>/memory/`, so a fact learned on one machine is there on the
next, and a wrong one gets corrected in a pull request like any other mistake.

```bash
loadout memory write build-quirks --project starstats \
  --description "things that surprise people about the build" \
  --fact "The first build after a clean takes four minutes; the analyzers warm up."
```

Only the index reaches the compiled context. Topics stay on disk with their
paths listed, because a project accumulates memory for years, and inlining all
of it would make every session pay for every fact anyone ever wrote down.

## Telling a session when to look

An index nobody opens is a cost with no return, and for months that is what it
was: across one project's whole transcript history, twenty-four titles sitting
in every context produced a single lookup in twelve thousand turns. The line
above them said "read the ones that bear on the task", which leaves a session
to notice mid-work that one of twenty-four titles might have applied.

The index now names the occasions instead:

> **Check this before you investigate.** Call `loadout_recall`, or read the
> file, before diagnosing a failure, before an unfamiliar error, before
> anything about how this project builds, tests, releases or is configured, and
> before recording a fact of your own so an existing topic is extended rather
> than contradicted. The store is small and a miss costs one call.

The `loadout_recall` tool description is written the same way — around when to
call it rather than around what it does — because a tool nothing calls is a tool
that isn't there.

The obvious alternative was tried and removed. A hook that searched memory on
every prompt and put the matches in front of the session unasked spoke on 56%
of 322 real prompts while only 18% of them had anything relevant to say, and
about a third of what it offered was a topically adjacent claim — which is the
kind of irrelevant context that costs most. Recall was already near its ceiling
at 91%, so the ranking was never the thing to tune. The index stays and the
guessing goes.

The repository stays authoritative. Where memory and the code disagree, the
code is right and the memory needs correcting.

## Finding the one that answers a question

```bash
loadout memory find "why did the release not publish to winget"
```

Same ranking the agent's `loadout_recall` uses, so what you see at the prompt is
what a session gets. It matches words rather than meanings and says so when it
finds nothing, which is the difference between "nobody wrote this down" and
"somebody wrote it down in other words".

Two rules do most of the work, and both exist because a plain count of matching
terms gets this wrong in the same way twice:

- **A long topic can't accumulate its way to the top.** Repetition saturates,
  scaled by how much the topic says relative to the average, so a topic of
  ordinary length scores exactly as it did and one twice that length has to say
  a term more often to be worth the same. Asked why a release didn't publish to
  winget, the store used to answer with its longest topic — an account of a
  stalling install check that happens to say "release", "publish" and "winget"
  somewhere inside a long account of something else.
- **Mentioning a subject is not being about it.** A name and a description are
  curated and say what a topic *is*; prose repeats a word for reasons that have
  nothing to do with its subject. Prose is saturated before it is weighed rather
  than after, so said once a term is worth most of a mention and said eight
  times barely more.

None of that settles retrieval, and the docs shouldn't pretend otherwise. What
guards it is a fixture of questions somebody actually asked, each paired with
the topic that answers it, run as a test against frozen copies of real topics —
a capability net for the cases past investigations turned on, not a measurement
of how often the ranking is right.

## Who a fact is true for

A store with one scope fills up with facts that aren't about the project.
"Restart Manager is disabled by policy", "spawned terminals inherit session
markers", "driving consoles kills live sessions" — file those under a project
and not one of them is about that project. Open another one and an agent
rediscovers them the expensive way.

```bash
loadout memory write upload-retries --scope project   # the default
loadout memory write review-habits --scope user
loadout memory write restart-manager --project starstats --scope machine
```

| Scope | Where it lives | Who it is true for |
|---|---|---|
| `project` | `projects/<slug>/memory/` | This project. Travels with the workspace |
| `user` | `memory/` at the workspace root | Your work, whatever the project. Travels too |
| `machine` | The machine-local state directory | This computer only. Never committed |

Two extra scopes rather than one global tier, and the difference is the point.
The workspace syncs between machines, so a fact that's true here and false on
the next one can't live in it. "The Restart Manager is disabled" is exactly
that, and a single global scope would spread it around as though it were
universal.
Where there's no machine-local store, a machine fact is refused rather than
written to the workspace instead: falling back would sync the one thing the
scope exists to keep local.

A session is subject to all three, so all three reach its index, and the two
that aren't about the project are labelled — an agent told "the Restart Manager
is disabled" needs to know that's a claim about the machine rather than about
the code it's reading. A project using only its own memory reads exactly as it
always has.

## Which project is this?

Every registered repository records its project in its own Git config, under
`loadout.project`. That file, `.git/config`, is per-clone and never committed,
so the mark adds nothing to the repository's contents. The rule that application
repositories hold application source only is about what gets committed, and a
tracked marker file would break it.

It's written whenever a project is registered, cloned or relocated, and
`loadout project link --all` fills it in for repositories registered before the
mark existed.

Resolution takes the recorded path first, then the mark, then the canonical
remote. That order matters. The path is this machine's own record of where a
project lives, so a directory you copied from somewhere else can't use its
inherited mark to answer to another repository's name. The mark earns its place
on the case the path can't cover: a repository that's been moved is still
recognised, instead of looking like one the launcher has never seen.

`loadout project survey` reports agent state on this machine that no project
accounts for, and says what each piece appears to belong to:

```text
D:\git\storefront-repos  7 topic(s)
  holds 2 repositories so this was recorded across all of them
    storefront-api
    storefront-web
  decide which project it belongs to, then: loadout memory import <project> --from ...
```

`--adopt` takes on whatever it can without a judgement call: importing memory for
a project that already exists, and registering a directory that's plainly one
repository before importing its memory. It previews first, asks per repository,
and takes a backup before writing, so you can accept one and decline its
neighbour.

It deliberately leaves the other cases alone. A directory holding several
repositories needs somebody to say which one the state describes. A directory
that isn't a repository at all can't be registered, and suggesting it would send
you to a command that can't succeed.

That last case is the one worth having. Agents key their state by the directory
they started in, and that isn't always a repository. Work done across several
repositories from their parent piles up memory against the parent, where it
describes all of them and belongs to none. The launcher names the candidates and
stops. Picking one would be a guess dressed as a fact, and the wrong guess files
a repository's hard-won notes under its neighbour.

## Compressing instructions into memory

The context compiler inlines instructions in full but memory only by its index.
A standing fact therefore costs a session the whole line on every launch while
it sits in instructions, and one index entry once it sits in memory.

`loadout memory compress <project>` moves the durable ones across:

```text
Would compress starplatform

  code-conventions          project, 16 fact(s)
  component-modularization  project, 10 fact(s)
  ...

Always loaded: 102 KB -> 67 KB (34 KB off every session)

Withheld 1 line(s) matching credentials in a URL, left in the instructions
rather than copied into the workspace repository.

Examined 178 list item(s). Left alone:
    46  makes no standing claim, so a later session has nothing to rely on.
```

Three rules keep it trustworthy. Content moves **verbatim and is never
reworded** — no model summarises anything, so the result cannot say something
the source did not. Nothing is removed from the source until it has been read
back out of the memory store. And only list items are considered: a bullet is a
self-contained claim that can be lifted without leaving a hole, where a
paragraph usually is not.

Candidates are screened for credentials first. The memory store screens too and
refuses a whole topic on one bad line, which is right for a direct write and
wrong here — one credential-shaped URL would otherwise block every good fact in
a large file. A withheld line stays exactly where it already was, disclosed no
further than it already was, and is reported by pattern name only.

What is left is prose, which `loadout rules split` scopes to paths instead.

## Adopting a project that already has memory

Several repositories were managed with an agent's own tooling before this
launcher existed, and their accumulated facts sit in a machine-local directory
nothing here reads. `loadout doctor` reports when it finds any, and
`loadout memory import` brings it across:

```bash
loadout memory import starstats                 # finds the agent's own layout
loadout memory import storefront --from <dir>   # or point at it directly
```

Topics are copied verbatim, never overwriting one already in the workspace, and
one holding something credential-shaped is refused rather than committed — the
workspace is a Git repository, so importing a token would publish it on the next
push. The original is copied rather than moved, so nothing is lost if the import
is wrong; removing the old copy is left to you.

### When the two copies disagree

A name in both stores used to end the question: the topic was called "already in
the workspace" and the run finished with "Nothing left to bring across", which
reads as an all clear. Two stores keeping the same topic names could therefore
disagree indefinitely. That wasn't hypothetical — one copy recorded the settled
cause of a suite failure while its twin still credited a fix that had been tried
and abandoned, and a session handed the stale one had no way to tell which it
was holding.

Topics are now compared on what they say — description and facts — rather than
on their bytes, because the frontmatter carries a modified stamp that changes on
every write and would otherwise report every topic as drifted.

```text
  skip    windows-install-check-stalls  differs from the workspace copy

2 topic(s) say something different here than in the workspace.
Nothing was overwritten. Compare each and settle which copy is right.
```

Named and left alone. Which copy is right is a judgement, not a merge, so the
import reports it, `--json` carries the same list under `drifted`, and nothing
is written either way.

Repositories organised this way also arrive with their instructions already
split into `.claude/rules/`. `loadout migrate` moves those to
`projects/<slug>/rules/` rather than into the agent's own directory: which
instructions apply to which paths is true whichever agent reads them, and the
rule loader only looks in the project's own rules directory. And `rules split`
refuses a file that something else has already split, recognising it by the fact
that it points at rule files rather than containing the detail itself — splitting
it again would rebuild those rules out of the summary left in their place.

Two checks keep memory worth loading:

- **Credentials are refused on write.** Memory is committed to a shared
  repository, so writing a token and flagging it afterwards would mean the
  disclosure had already happened. Findings name the *pattern* that matched and
  never the value.
- **Facts that will rot are reported.** An account of a change ("added a retry
  to the upload step") belongs in the repository history and reads as present
  tense forever; a fact dated to the day it was written ("the highest migration
  is 0052") misleads within weeks. `loadout memory audit` reports those along
  with duplicates, oversize topics, stale entries and index rot.

  Two of those classes are warnings rather than asides, and only two: a fact
  pinned to the moment it was written, and a fact already past its own date.
  They're the ones that turn from true into misleading with nothing in the store
  to catch them, so they hold up the verdict instead of sitting under a word
  that says the store is fine — which is how a memory claiming a submission was
  blocked on a signature it had already received stayed wrong for two days
  behind a `HEALTHY`. Everything else — weak phrasing, a vague description, a
  dead cross-reference — stays information, because an audit that demands
  attention for every imperfect sentence is one people stop reading, and that
  costs more than the sentences do.
- **A second topic on the same ground is stopped, and the first one named.** This
  is how memory comes to contradict itself: nothing is overwritten, both are
  indexed, and a later session gets two answers with nothing to choose between
  them. Contradictions arrive one fact at a time, at the moment something could
  have been shown — so that's when it's shown. Add the fact to the topic named
  back to you, or pass `--separate` when it really is a different subject.
  Writing to a topic that already exists is never questioned: extending is the
  thing this exists to encourage. Two shared words are needed, not one, because
  a check that interrupts every write is one whose override becomes a habit.
- **A description that can't be chosen from is refused.** Only the index reaches
  a session's context — one name and one line per topic — so that line is the
  whole basis for deciding whether to open the topic. "notes", or the topic's
  own name said back, costs a session's attention on every launch and tells it
  nothing, and the topic goes unread whatever is in it. `memory write` refuses
  one before it is written and the audit reports the ones already there. What it
  never judges is whether the description is *true*: that isn't checkable, and a
  regular expression claiming to do it would be guessing with a straight face.

`loadout memory audit --clean` removes what can be removed without judgement:
topics holding no facts, facts repeated word for word, and index lines pointing
at files that are gone. It never rewrites prose and never merges two facts that
merely say similar things — deciding which wording is the right one is the
judgement a tool should not be making on somebody's behalf. A backup is taken
first, and `--apply` is required to change anything.

## Filling a memory that's empty

A project you registered today has no memory, and nothing here writes one for
you. Loadout measures things; deciding what a codebase means is a job for
whoever — or whatever — is reading it.

What the library does ship is the procedure. `skill.repository-review` activates
on a task like "review the repo", "learn this codebase", "onboard" or "get up to
speed", and tells the agent to write down what it finds instead of leaving it in
the conversation:

```bash
loadout launch starstats --mode investigate   --task "review this codebase and record what you find"
loadout memory list starstats                   # then see what it left behind
```

You need the mode, and leaving it off is the one way to get nothing. Modes
aren't guessed from what you typed. `--mode` defaults to `implement`, which
assumes you've already decided what to do, so review skills don't load. Check
before you spend a session on it:

```bash
loadout instructions explain --project starstats "review the repo"
loadout instructions explain --project starstats "review the repo" --mode investigate
```

Run both. The first lists no skill. The second lists Repository review and the
phrase that reached it.

The procedure starts by reading what's already known — `instructions explain`,
`instructions audit`, `rules budget` and the existing memory — because
re-deriving a fact somebody already wrote down is the commonest waste there is.
It asks for one change traced end to end, and for every claim to be checked by
running it rather than guessed from a name.

It's just as clear about what not to record: anything that'll be false next
month, anything the code already says plainly, and anything the credential
screen would refuse. A confidently wrong memory costs far more than a missing
one, so it asks you to extend or delete an existing topic rather than add a
second one next to it.

Agents launched with the launcher's own tools can write findings as they go with
`loadout_remember`, without leaving the session. It asks for the description
rather than inventing one: what it generated before said that an agent had
recorded something, which is the one thing a later session can already see.

