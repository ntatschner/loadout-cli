---
id: framework.react
kind: framework
title: React
summary: Rendering, state ownership and effects.
globs:
  - '**/*.tsx'
  - '**/*.jsx'
dependencies:
  - '"react"'
  - 'react-dom'
task_phrases:
  - 'react'
  - 'component'
  - 'usestate'
  - 'useeffect'
requires:
  - 'language.typescript'
---

## Cares about

What causes a render, and what runs after one.

## Working rules

- Keep state as close to where it is used as possible; lift only when shared.
- Effects synchronise with something outside React. Deriving state in an effect
  is usually a render-time computation instead.
- Give every effect a correct dependency list and a cleanup where it subscribes.
- Keys must be stable and identify the item, not its position.

## Pitfalls

- An effect that sets state it also depends on, looping.
- A new object or function in props defeating memoisation.
- `useEffect` fetching without cancelling on unmount.

## Verify

Test behaviour through the rendered output, not internals. For anything about
re-renders, measure with the profiler.

## Defer to

`function.frontend` for bundle and asset questions;
`function.accessibility` for anything users interact with.
