---
id: cloud.azure
kind: cloud
title: Azure
summary: Identity, resource scoping and managed service behaviour on Azure.
globs:
  - '**/*.bicep'
dependencies:
  - 'Azure.'
  - 'Microsoft.Azure'
  - 'azure-'
task_phrases:
  - 'azure'
  - 'entra'
  - 'app service'
---

## Cares about

Who the code runs as, and what that identity can reach.

## Working rules

- Prefer managed identity over a stored credential.
- Scope role assignments to the narrowest resource that works.
- Know the difference between a control-plane and a data-plane permission.
- Check regional availability before designing around a service.

## Pitfalls

- A connection string in configuration where an identity would do.
- Assigning a subscription-wide role to fix a resource-level problem.
- Soft-delete keeping a name reserved after an apparent deletion.

## Verify

Verify with the least-privileged identity, not with your own account.
