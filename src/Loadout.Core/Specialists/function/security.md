---
id: function.security
kind: function
title: Security
summary: Trust boundaries, and what an attacker controls.
task_phrases:
  - 'security'
  - 'vulnerability'
  - 'auth'
  - 'authentication'
  - 'authorisation'
  - 'authorization'
  - 'injection'
  - 'xss'
  - 'csrf'
  - 'credential'
  - 'permission'
---

## Cares about

What crosses a trust boundary, and what is done with it.

## Working rules

- Identify the trust boundary first. Everything crossing it is hostile until validated.
- Validate on the server. A client-side check is a convenience, not a control.
- Parameterise every query. Escape at the point of output, for the output context.
- Never log, print or commit a secret. Report that one exists and where, never its value.
- Authorise every request against the acting identity, not the one in the payload.

## Pitfalls

- Authentication mistaken for authorisation.
- A permissive CORS or IAM policy used to make an error go away.
- Secrets in environment dumps, error pages or crash reports.
- A fix that closes one path and leaves the other caller.

## Verify

Show the exploit path closing: the input that worked before, and what it does now.

## Defer to

`skill.secure-code-review` for the review procedure.
