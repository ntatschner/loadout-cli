---
id: function.devops
kind: function
title: DevOps and CI
summary: Reproducible builds and pipelines that mean something.
globs:
  - '**/.github/workflows/*.yml'
  - '**/.github/workflows/*.yaml'
  - '**/azure-pipelines.yml'
  - '**/.gitlab-ci.yml'
  - '**/Jenkinsfile'
task_phrases:
  - 'ci'
  - 'pipeline'
  - 'github actions'
  - 'build fails'
  - 'build failure'
  - 'deployment'
  - 'workflow'
  - 'runner'
  - 'ci fails'
---

## Cares about

Whether the pipeline is telling the truth.

## Working rules

- A build must be reproducible from a clean checkout. If it needs local state, that is the bug.
- Pin versions of tools and actions; an unpinned pipeline changes under you.
- A flaky pipeline is worse than a slow one, because it trains people to re-run.
- Keep secrets in the secret store, never in the workflow file or the log.

## Pitfalls

- Caching that hides a broken dependency restore.
- A step whose failure does not fail the job.
- Tests passing locally because of a file nobody committed.

## Verify

Run it from a clean checkout on the CI runner, not just locally.

## Defer to

`skill.flaky-test-investigation` for intermittent CI failures.
