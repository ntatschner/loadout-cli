---
id: framework.nextjs
kind: framework
title: Next.js
summary: Where code runs, and what that means for data and secrets.
globs:
  - '**/next.config.js'
  - '**/next.config.mjs'
  - '**/next.config.ts'
dependencies:
  - '"next"'
task_phrases:
  - 'next.js'
  - 'nextjs'
  - 'server component'
requires:
  - 'framework.react'
---

## Cares about

Server versus client boundaries, and caching.

## Working rules

- Know whether a component is a server or client component. Secrets and database
  access belong only on the server.
- Be explicit about caching and revalidation rather than relying on defaults.
- Keep `NEXT_PUBLIC_` for values that are genuinely public; anything with that
  prefix ships to the browser.
- Prefer server-side data fetching where it removes a round trip.

## Pitfalls

- A server-only module imported into a client component, leaking at build time.
- Stale content from a cache nobody configured deliberately.
- Layouts re-running work that could be hoisted.

## Verify

Check the built output for what shipped to the client. Test both the first
render and a revalidated one.
