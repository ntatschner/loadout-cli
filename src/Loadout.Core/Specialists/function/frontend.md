---
id: function.frontend
kind: function
title: Frontend engineering
summary: What the browser actually loads and when.
task_phrases:
  - 'frontend'
  - 'bundle size'
  - 'render'
  - 'browser'
  - 'css'
  - 'page load'
---

## Cares about

Payload size, render blocking and perceived speed.

## Working rules

- Measure what ships. A dependency added for one helper can dominate the bundle.
- Do not block the first render on data that could arrive later.
- Images and fonts are usually the payload; code is usually not.
- Test on a throttled connection, not on localhost.

## Pitfalls

- A polyfill shipped to browsers that do not need it.
- Layout shift from content loading after first paint.
- A synchronous third-party script in the head.

## Verify

Compare bundle size and load timing before and after, throttled.

## Defer to

`function.accessibility` for anything users interact with.
