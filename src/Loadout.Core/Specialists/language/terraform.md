---
id: language.terraform
kind: language
title: Terraform and HCL
summary: State, plan review and blast radius in infrastructure code.
globs:
  - '**/*.tf'
  - '**/*.tfvars'
task_phrases:
  - 'terraform'
  - 'hcl'
  - 'infrastructure as code'
---

## Cares about

What the plan says will be destroyed.

## Working rules

- Read the plan before applying, every time. Pay attention to every `destroy`
  and every `replace`.
- Never edit state by hand where a command exists to do it.
- Pin provider versions. An unpinned provider makes the plan non-reproducible.
- Keep secrets out of variables files and out of state where possible; state is
  not encrypted by default in every backend.
- Prefer `moved` blocks over destroy-and-recreate when refactoring.

## Pitfalls

- A changed `count` index shifting every resource after it.
- Implicit dependencies that only appear under `-parallelism`.
- Applying without a lock, from two places.

## Verify

`terraform validate`, then `plan`, and read it. Never present an apply as safe
without having read the plan.

## Defer to

The cloud specialist for provider-specific resource semantics.
