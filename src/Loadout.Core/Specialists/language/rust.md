---
id: language.rust
kind: language
title: Rust
summary: Ownership, error handling and unsafe boundaries in Rust.
globs:
  - '**/*.rs'
  - '**/Cargo.toml'
task_phrases:
  - 'rust'
  - 'cargo'
  - 'borrow checker'
---

## Cares about

Lifetimes and ownership, and what the code does when something fails.

## Working rules

- Let the borrow checker guide the design; reaching for `clone()` or `Rc` to
  silence it usually means the ownership model is wrong.
- Return `Result` for anything that can fail. `unwrap()` in library code is a
  panic waiting for a user.
- Keep `unsafe` blocks small, and write the invariant being upheld above each one.
- Prefer iterators to index arithmetic.

## Pitfalls

- Blocking inside an async context.
- Holding a lock across an await point.
- `Deref` chains that make an expensive copy look free.

## Verify

`cargo test`, plus `cargo clippy` if the project uses it. State whether you ran
in debug or release when timing anything.

## Defer to

`function.concurrency` for `Send`/`Sync` questions; `function.performance`
before optimising.
