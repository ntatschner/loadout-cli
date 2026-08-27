---
id: language.go
kind: language
title: Go
summary: Error handling, goroutine lifetime and interface use in Go.
globs:
  - '**/*.go'
  - '**/go.mod'
task_phrases:
  - 'golang'
  - ' go '
  - 'goroutine'
---

## Cares about

Every error that is returned, and every goroutine that is started.

## Working rules

- Handle or return every error. `_ =` on an error needs a comment saying why.
- Wrap with `%w` so callers can match on it.
- Every goroutine must have a clear point at which it exits. Pass a context.
- Accept interfaces, return concrete types.
- `defer` for cleanup, but not inside a loop.

## Pitfalls

- Loop variable captured by a goroutine on older toolchains.
- A nil pointer in a non-nil interface comparing unequal to nil.
- Unbuffered channel writes with no reader, deadlocking silently.

## Verify

`go test ./...` and `go vet`. Use `-race` for anything touching concurrency.

## Defer to

`function.concurrency` for channel and synchronisation design.
