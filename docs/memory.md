# Memory

Memory holds durable facts a session should not have to rediscover:
architecture, decisions and why they were made, non-obvious build behaviour,
traps that keep catching people. It lives in the workspace repository at
`projects/<slug>/memory/`, so a fact learned on one machine is available on the
next and a wrong one can be corrected in a pull request like any other mistake.

```bash
loadout memory write starstats build-quirks \
  --description "things that surprise people about the build" \
  --fact "The first build after a clean takes four minutes; the analyzers warm up."
```

Only the index reaches the compiled context. Topics stay on disk with their
paths listed, because a project accumulates memory for years and inlining all of
it would make every session pay for every fact anyone ever recorded.

## Which project is this?

Every registered repository records its project in its own Git config, under
`loadout.project`. That file, `.git/config`, is per-clone and is never
committed, so the mark adds nothing to the repository's contents — the rule that
application repositories hold application source only is about what gets
committed, and a tracked marker file would breach it.

It is written whenever a project is registered, cloned or relocated;
`loadout project link --all` fills it in for repositories registered before the
mark existed.

Resolution takes the recorded path first, then the mark, then the canonical
remote. The order matters: the path is this machine's own record of where a
project lives, so a directory copied from elsewhere cannot use its inherited
mark to answer to another repository's name. The mark earns its place in the
case the path cannot cover — a repository that has been moved is still
recognised, rather than looking like one the launcher has never seen.

`loadout project survey` reports agent state on this machine that no project
accounts for, and says what each piece appears to belong to:

```text
D:\git\storefront-repos  7 topic(s)
  holds 2 repositories so this was recorded across all of them
    storefront-api
    storefront-web
  decide which project it belongs to, then: loadout memory import <project> --from ...
```

`--adopt` takes on what can be taken on without a judgement call: importing
memory for a project that already exists, and registering a repository that is
plainly one repository before importing its memory. It previews first, asks per
repository, and takes a backup before writing, so one can be accepted and its
neighbour declined.

It deliberately never touches the other cases. A directory holding several
repositories needs somebody to say which one the state describes; a directory
that is not a repository at all cannot be registered, and suggesting it would
send you to a command that cannot succeed.

That last case is the one worth having. Agents key their state by the directory
they were started in, which is not always a repository: work done across several
repositories from their parent accumulates memory against the parent, where it
describes all of them and belongs to none. The launcher names the candidates and
stops. Picking one would be a guess presented as a fact, and the wrong guess
files a repository's hard-won notes under its neighbour.

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

