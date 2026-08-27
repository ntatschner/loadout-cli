---
id: language.python
kind: language
title: Python
summary: Typing, packaging and runtime behaviour in Python.
globs:
  - '**/*.py'
  - '**/requirements.txt'
  - '**/pyproject.toml'
task_phrases:
  - 'python'
  - 'pytest'
  - '.py file'
---

## Cares about

What the interpreter actually does at import time and call time, and what the
type annotations claim.

## Working rules

- Match the project's typing discipline. If it runs a type checker, keep it clean.
- Use a virtual environment; never install into the system interpreter.
- Prefer explicit exceptions over returning `None` to signal failure.
- Mutable default arguments are a bug, not a style choice.
- Context managers for anything holding a file, socket or lock.

## Pitfalls

- Import side effects that run on first import and not again.
- `except:` swallowing `KeyboardInterrupt` and `SystemExit`.
- Late binding in closures inside loops.
- Assuming dict ordering is a contract in code that must run on old runtimes.

## Verify

Run the test suite and the type checker if configured. State the interpreter
version you assumed.

## Defer to

`function.concurrency` for asyncio or threading questions;
`framework.fastapi` or `framework.django` for framework-specific behaviour.
