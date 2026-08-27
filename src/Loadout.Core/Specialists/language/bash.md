---
id: language.bash
kind: language
title: Bash and POSIX shell
summary: Quoting, failure modes and portability in shell scripts.
globs:
  - '**/*.sh'
  - '**/*.bash'
task_phrases:
  - 'bash'
  - 'shell script'
  - 'posix sh'
---

## Cares about

Unquoted expansions and commands whose failure nobody notices.

## Working rules

- `set -euo pipefail` at the top of any non-trivial bash script, and know what
  each part changes.
- Quote every expansion. `"$var"`, `"$@"`, `"${arr[@]}"`.
- Use `$(...)`, never backticks.
- Check whether the script must be POSIX `sh`; if so, no arrays, no `[[`.

## Pitfalls

- A pipeline's exit status coming from the last command only.
- `cd` failing and the next command running in the wrong directory.
- Word splitting on filenames containing spaces.
- `rm -rf "$dir/"` where `$dir` was empty.

## Verify

Run `shellcheck` if available. Dry-run destructive scripts with `echo` in front
of the destructive command.

## Defer to

`platform.linux` for system behaviour; `platform.windows` where the script must
also run under Git Bash or WSL.
