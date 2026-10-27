---
id: skill.actually-done
kind: skill
title: Actually done
summary: A procedure for answering whether work is finished, condition by condition.
task_phrases:
  - 'is it done'
  - 'is this done'
  - 'are we done'
  - 'actually done'
  - 'is it finished'
  - 'is this finished'
  - 'check it is finished'
  - 'check it is done'
---

## When to use

Somebody asks whether a piece of work is finished. The answer they need is not
a feeling about the work but a statement about each thing it was meant to
achieve. `foundation.verification` covers testing a change; this covers the
question asked afterwards.

## Procedure

1. List the done-when conditions before checking any of them. Take them from
   the brief, the task record (`loadout task show <id>`), or the request
   itself. Where none were written down, say so, and list the ones the request
   implies, marked as your reading.
2. For each condition, choose a command whose output would show it met or
   unmet: a test filter, a build, a `grep`, the command under question run with
   real input.
3. Run it now. A result from an earlier turn, or from memory, is not a result.
4. Quote the command and the output it actually printed, against the condition
   it checks.
5. Never mark a condition met from reading code. Reading says what the code
   should do; only running it says what it does.
6. Where nothing can be run to check a condition — a person has to look, a
   remote has to be reached, CI has to finish — mark it unchecked and say what
   would check it.
7. Answer with the list: each condition, met, unmet or unchecked, with its
   evidence. Never a single "done".

## Done when

- Every condition is listed, with where it came from.
- Every met or unmet verdict has a command and its quoted output beside it.
- Every unchecked condition names what would check it.
- The answer is the list, not a one-word verdict.
