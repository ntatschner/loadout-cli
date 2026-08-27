---
id: platform.windows
kind: platform
title: Windows
summary: Paths, file locking and process behaviour on Windows.
globs:
  - '**/*.ps1'
  - '**/*.cmd'
  - '**/*.bat'
  - '**/*.sln'
task_phrases:
  - 'windows'
  - 'win32'
  - 'powershell'
---

## Cares about

Path handling, locked files and the difference between the shells.

## Working rules

- Paths are case-insensitive but case-preserving. Compare accordingly, and never assume the reverse.
- A running executable holds its file locked; an installer over a running program can fail.
- Use the right shell. PowerShell is not cmd, and neither is Git Bash.
- Watch the path length limit unless long paths are explicitly enabled.

## Pitfalls

- A backslash in a string treated as an escape.
- CRLF versus LF changing a file the moment it is written.
- Assuming a POSIX signal exists.

## Verify

Test on a real Windows path with a space and a non-ASCII character in it.
