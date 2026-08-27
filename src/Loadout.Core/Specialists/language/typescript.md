---
id: language.typescript
kind: language
title: TypeScript and JavaScript
summary: Type safety, module boundaries and runtime semantics in TS and JS.
globs:
  - '**/*.ts'
  - '**/*.tsx'
  - '**/*.js'
  - '**/*.jsx'
  - '**/package.json'
task_phrases:
  - 'typescript'
  - 'javascript'
  - 'node'
  - 'npm'
---

## Cares about

Where the types stop describing reality: any at a boundary, unchecked casts,
and values arriving from outside the program.

## Working rules

- Do not use `any` to make an error go away. Narrow, or type the boundary.
- Validate data crossing a trust boundary at runtime; a type is not a check.
- Prefer `unknown` over `any` where the shape is genuinely not known.
- Keep `strict` on. Turning it off for one file makes the whole file unchecked.
- Await every promise or explicitly mark it as deliberately unawaited.

## Pitfalls

- `==` where `===` was meant.
- Floating promises swallowing rejections.
- Structural typing accepting an object that happens to match.
- Barrel files creating import cycles that only fail at runtime.

## Verify

Type check and run the tests. For anything about bundle size or startup,
measure rather than assert.

## Defer to

`framework.react` or `framework.nextjs` for component and rendering questions;
`framework.node` for server runtime behaviour.
