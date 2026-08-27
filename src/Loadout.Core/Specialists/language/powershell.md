---
id: language.powershell
kind: language
title: PowerShell
summary: Pipeline behaviour, error actions and cross-edition portability.
globs:
  - '**/*.ps1'
  - '**/*.psm1'
  - '**/*.psd1'
task_phrases:
  - 'powershell'
  - 'pwsh'
  - 'cmdlet'
---

## Cares about

What actually stops a script, and what silently continues.

## Working rules

- `-ErrorAction SilentlyContinue` hides the message, not the failure. Use
  `try { ... -ErrorAction Stop } catch { }` when a failure is genuinely fine.
- Use approved verbs for functions; `Get-Verb` lists them.
- Quote paths that may contain spaces; use the call operator for executables.
- Prefer `[CmdletBinding()]` with `-WhatIf` support for anything destructive.

## Pitfalls

- Assuming Windows PowerShell 5.1 features exist in `pwsh`, or the reverse.
- Output accidentally polluted by an uncaptured cmdlet result.
- `$?` and `$LASTEXITCODE` meaning different things.

## Verify

Run with `-WhatIf` first for anything that changes state. State which edition
you targeted.

## Defer to

`platform.windows` for Windows-specific behaviour.
