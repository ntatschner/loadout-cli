---
id: platform.linux
kind: platform
title: Linux
summary: Permissions, signals and filesystem behaviour on Linux.
globs:
  - '**/Makefile'
  - '**/*.service'
  - '**/.bashrc'
task_phrases:
  - 'linux'
  - 'ubuntu'
  - 'debian'
  - 'systemd'
---

## Cares about

File modes, process signals, and a case-sensitive filesystem.

## Working rules

- Set permissions deliberately on anything holding secrets: 0600 for files, 0700 for directories.
- Handle SIGTERM for orderly shutdown; SIGKILL cannot be caught.
- Paths are case-sensitive. Do not assume a name matches in a different case.
- Prefer XDG base directories over inventing dotfile locations.

## Pitfalls

- A script assuming GNU coreutils behaviour on a BusyBox system.
- A file created before umask is considered, ending up world-readable.
- A child process outliving its parent because nothing reaps it.

## Verify

Check the actual mode bits with `stat`, not what you intended to set.
