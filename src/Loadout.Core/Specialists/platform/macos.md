---
id: platform.macos
kind: platform
title: macOS
summary: Filesystem case, code signing and Apple-specific paths.
globs:
  - '**/*.entitlements'
  - '**/Info.plist'
task_phrases:
  - 'macos'
  - 'mac os'
  - 'darwin'
  - 'apple silicon'
---

## Cares about

That APFS ships both case-sensitive and case-insensitive, and that binaries need signing.

## Working rules

- Never assume the filesystem is case-insensitive; probe rather than guess.
- Normalise Unicode filenames to NFC before comparing; the filesystem may hand back NFD.
- Use ~/Library paths, not XDG, unless the user has opted in.
- Unsigned binaries are blocked by Gatekeeper; signing and notarisation are separate steps.

## Pitfalls

- A path comparison that works on one Mac and not another.
- Homebrew assumed present, or assumed at one prefix on both architectures.
- Rosetta masking an architecture problem.

## Verify

Test on Apple Silicon natively, not under emulation.
