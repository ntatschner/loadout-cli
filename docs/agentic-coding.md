# Working with a coding agent

Most of what goes wrong with a coding agent isn't the model. It's that it was
never told something it needed, or it was told so much that the part that
mattered was buried, or it wrote down nothing and the next session started from
scratch. That's the same three problems whether you started last week or have
been doing this all year — what changes is which one is biting you.

Three sections, by where you are. Skip to the one that sounds like you. The
Loadout commands are named where they're relevant, but the habits are the point
and they hold whatever you launch with.

## If you've never used one

### What it actually is

A model in a loop with tools. It reads your files, writes to them, runs
commands, reads what came back, and decides what to do next. It isn't a search
engine with a nicer interface, and it isn't a colleague — it has no memory of
yesterday and no opinion about your deadline.

Three things follow from that, and nearly everything else is a consequence of
one of them.

**It only knows what's in front of it.** The context window is the whole world
for that session: your prompt, the files it has read, the instructions it was
launched with. It cannot see the thing you didn't mention. When it does
something that looks stupid, the first question is what it was actually given,
not what you assumed it knew.

**It forgets.** Close the session and everything it worked out is gone unless
something wrote it down. Working out the same non-obvious thing four times is
the commonest waste there is, and it's invisible because each session looks fine
on its own.

**It is confidently wrong at the same volume it is right.** There's no tone
change between a fact and an invention. Nothing in the loop checks it for you,
so verification is yours, and an agent that can run your tests is verifying more
than one that can't.

### Start here

Pick something small and checkable. "Add a retry to the upload step, and a test
for it" is a good first task: you can read the diff, you can run the test, and
you'll know inside five minutes whether it did what you asked.

Commit before you start. Not as ceremony — it's what lets you throw the whole
attempt away without discussion. `git diff` and `git checkout .` are the two
most useful commands in agentic coding and neither has anything to do with AI.

Read what it changed before you accept it. All of it, the first several times.
The thing you're calibrating is how much you trust it for this kind of work, and
you can't calibrate that from a summary it wrote about itself.

Say what you're doing, not just what you want. "Why does the upload retry twice
then give up" and "add a retry to the upload step" want completely different
things from a session, and a good launcher can act on that difference — but only
if you say it.

### What Loadout does about it

The launch sheet shows what the session will be told before it starts: which
guidance, why each piece was picked, what it costs. `--dry-run` on any launch
describes the whole thing and starts nothing.

`loadout protect` keeps the agent's own files — instruction files, rules
directories, session state — out of your repository, so your commits stay
yours and a teammate who has never installed any of this sees a clean diff.

And `loadout instructions explain "what you asked for"` answers the "what was it
actually given" question, which is the one you'll want most often in the first
fortnight.

## If you've used one for a few weeks

You've got past the novelty and hit the real problems: it's plausible and wrong
sometimes, it wanders off scope, and every new session starts from nothing.

### One session, one job

A session that's been running for three hours has a context window full of
things that were true two hours ago. Long sessions don't get smarter; they get
more crowded, and the model spends more of its attention on its own history than
on your code.

So finish a job, write down anything worth keeping, and start a new session for
the next one. Resume when the work genuinely continues — `loadout resume` brings
back the task, mode, profile and worktree the session launched with, so it
reopens with the guidance it had rather than as a bare transcript.

### Give it something that can fail

The single biggest difference between a session that produces work you can trust
and one that produces plausible text is whether the agent could check itself. A
test it can run, a build that breaks, a command with a non-zero exit — these turn
"I've made the change" into something with evidence behind it.

If the check is slow or awkward to run, say so up front. An agent that doesn't
know how to run your tests will confidently tell you it's done.

### Mind what's loaded on every turn

Anything always-loaded is paid for on every turn of every session, whatever the
task. That's where instruction files quietly go wrong: a document that started as
half a page of house style becomes six pages of everything anyone ever wanted to
say, and the part that matters for today's work is one paragraph in the middle.

Two ways out, and they're both about scope rather than deletion. Guidance that
applies to certain paths should say so, so it loads when the work touches them
and not otherwise. Standing facts belong in a store the session can look things
up in, not in a document it re-reads every launch.

```sh
loadout rules budget starstats     # what every session pays for
loadout rules audit starstats      # what costs tokens invisibly
loadout rules split starstats      # scope prose to the paths it applies to
loadout memory compress starstats  # standing facts out of always-loaded files
```

### Write down what a session worked out

Decisions and why they were made, constraints, the traps that keep catching
people. Not a changelog — the repository already has one, and "added a retry to
the upload step" reads as present tense forever once it's in a memory store.

```sh
loadout memory write --project starstats <topic> --description "..." --fact "..."
loadout handoff starstats     # what the next session needs to pick this up
```

A confidently wrong memory costs more than a missing one, which is why
`loadout memory audit` exists and why a fact pinned to the day it was written is
a warning rather than a footnote.

### Say what you're doing, in the field that's for it

The task line is the highest-confidence input a resolver has, and it's the one
that sits empty. Everything else has to be inferred from file extensions and
defaults, which gets you the general answer rather than the one for today.

```sh
loadout starstats --mode investigate \
  --task "the release workflow keeps failing on windows"
```

That sentence reaches Windows, debugging, CI and release guidance. "work on the
release" reaches none of them.

## If you run them every day

At this point the bottleneck isn't the model and isn't your prompting. It's that
you're maintaining a body of guidance, a store of facts and a token bill, all
three of which rot quietly, and none of which tell you they've rotted.

### Stop believing the setup works and measure it

The uncomfortable pattern, seen repeatedly in this project's own history: a
feature that reads perfectly and is used once in twelve thousand turns. A memory
index sat in every compiled context for months and was opened once, because the
line above it said "read the ones that bear on the task" and left the session to
notice mid-work that one of twenty-four titles might apply. It names the
occasions to look now instead — and whether that works here isn't known yet
either, because the honest check is the same one that condemned the version
before it: count the lookups over the next stretch of real work.

So count things rather than reasoning about them:

```sh
loadout instructions stats            # which specialists launches actually reached
loadout instructions probe <id>       # how often sessions did what one asks for
loadout launches starstats            # what each session was told to be
loadout usage --days 30 --by project  # what it cost
```

The two most useful questions are which guidance nothing has ever loaded, and
which loaded guidance changed nothing about what the session did. Both of those
are paid for on every launch.

### Be suspicious of measurements you invented

Six questions you thought of while changing a ranking will agree with you. In
this project, six invented queries said a retrieval hook was being selective.
Measured instead against 322 prompts taken from real transcripts, it spoke on
56% of them while only 18% had anything relevant to say — and the feature was
removed rather than tuned, because what it produced when wrong was a topically
adjacent claim, which is the most damaging kind of irrelevant context there is.

If you're tuning something that decides what a session sees, get the inputs from
traffic you didn't write.

### Keep the store honest

Memory goes wrong in ways that don't look like errors. Two copies of the same
topic drift apart and both read fine. A fact that was true in September is still
stated flatly in November. A second topic gets written beside the first instead
of extending it, and a later session is handed two answers with nothing to
choose between them.

```sh
loadout memory audit starstats        # secrets, duplicates, staleness, index rot
loadout memory review starstats       # walk what nobody has revisited
loadout memory import starstats --dry-run   # what another store says differently
```

None of those merge anything, and that's deliberate. Which of two accounts is
right is a judgement, and a tool that picked one would be guessing with a
straight face.

### Guidance is a thing you maintain, not a thing you wrote once

Once several people or several repositories are involved, house style needs to
live somewhere shared and be versioned like anything else. Loadout's answer is
the workspace: specialists, rules, memory and project manifests in one
repository, with `loadout share candidates` finding the guidance that's escaped
into one project and belongs to everybody, and `loadout pack list` for sets
fetched from a Git remote and approved per machine.

Path-scoped rules matter more the bigger the codebase gets. A rule about
migrations that loads on frontend work is not neutral — it's noise competing for
attention with the guidance that applies.

### Know what still needs you

Deciding which of two contradictory accounts is right. Judging whether a
description is true, as opposed to well-formed. Saying which of three
architectures the codebase is going to live with. Reading the diff before it
becomes a commit.

Tools can measure, report and refuse. The judgement is the part that stays
yours, and a setup that pretends otherwise is one that quietly makes decisions
in your name.

## See also

- [Getting started](getting-started.md) — the first hour with Loadout, in order
- [The context budget](context-budget.md) — what loads when, and what it costs
- [Specialists and skills](specialists.md) — how an instruction set gets composed
- [Memory](memory.md) — recording, finding, compressing and auditing facts
