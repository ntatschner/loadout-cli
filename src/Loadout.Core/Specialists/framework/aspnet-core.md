---
id: framework.aspnet-core
kind: framework
title: ASP.NET Core
summary: Middleware order, model binding and endpoint behaviour.
globs:
  - '**/Program.cs'
  - '**/Startup.cs'
dependencies:
  - 'Microsoft.AspNetCore'
task_phrases:
  - 'asp.net'
  - 'aspnet'
  - 'middleware'
  - 'controller'
requires:
  - 'framework.dotnet'
---

## Cares about

The order things run in, and what reaches a handler unvalidated.

## Working rules

- Middleware order is behaviour. Authentication before authorisation, both
  before endpoints.
- Validate models at the boundary; do not trust bound input.
- Return the right status code. A failure returning 200 with an error body is a
  contract problem.
- Keep controllers thin; put decisions where they can be tested without a host.

## Pitfalls

- CORS configured permissively to make a browser error go away.
- Exception middleware leaking stack traces in production.
- Async handlers blocking on synchronous I/O.

## Verify

Exercise the endpoint end to end, including the failure path and its status code.

## Defer to

`function.security` for authentication and authorisation changes;
`function.api` for contract shape.
