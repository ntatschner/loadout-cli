---
id: skill.write-project-specialist
kind: skill
title: Writing a project's own specialist
summary: Turning how a codebase actually works into guidance a session can follow.
task_phrases:
  - 'project specialist'
  - 'house style'
  - 'write the conventions'
  - 'document the conventions'
  - 'coding standards for this'
---

## What this is for

The built-in library knows about the language and the framework. It cannot know
that *this* codebase returns a result type rather than throwing, names its tests
in a particular shape, or explains its non-obvious choices in a doc comment
above the member rather than a line comment beside it. That is the guidance that
stops an agent writing code which reads as foreign, and nothing detects it.

`loadout instructions new <id> --project <slug>` drafts the file and fills in
what can be counted: the test framework, the assertion library, whether nullable
is on, how often failure is returned against how often it is thrown, how much of
the code carries doc comments. Those lines are already true. Everything else in
the draft is a prompt.

## Working rules

- **Read before writing.** Open a dozen files from different parts of the tree,
  including tests. A convention you find once is an accident; one you find in
  every file is the house style.
- **Check the measured lines rather than trusting them.** They were counted by
  pattern matching. If "returns a result type nine times as often as it throws"
  does not match what you just read, the count is measuring something you have
  not understood yet — find out which before writing either down.
- **Write instructions, not description.** "Errors are returned rather than
  thrown" tells a session what to do. "This codebase has an interesting error
  handling approach" does not.
- **Say why, where the why is not obvious.** A rule with a reason survives
  somebody disagreeing with it; a rule without one gets argued with, or quietly
  dropped.
- **Prefer what would be got wrong.** Guidance that repeats what any competent
  session would do anyway is paid for on every launch and changes nothing.

## What not to write

- Anything the language specialist already says. `language.csharp` covers
  nullable, async and disposal; repeating it doubles the cost and creates two
  places to keep in step.
- Anything that will be false next month. A file layout mid-migration, a
  dependency about to be replaced, a rule nobody has agreed to yet.
- Anything the code says plainly. A session can read the code; it cannot read
  the argument that produced it.
- A count you have not checked. The draft's figures are evidence, not a
  conclusion, and shipping one you did not verify is how a wrong fact becomes a
  standing instruction.

## Verify

Ask `loadout instructions explain --project <slug>` for a task the specialist
should apply to, and check it is loaded and for the reason you expected. Then
read the guidance back as though you had not written it: if a line does not
change what a session would do, it is costing tokens and buying nothing.
