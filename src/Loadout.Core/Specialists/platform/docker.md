---
id: platform.docker
kind: platform
title: Docker
summary: Image layers, build context and container runtime behaviour.
globs:
  - '**/Dockerfile'
  - '**/docker-compose.yml'
  - '**/docker-compose.yaml'
  - '**/.dockerignore'
task_phrases:
  - 'docker'
  - 'container'
  - 'dockerfile'
---

## Cares about

What ends up in the image, and what the container can reach.

## Working rules

- Order layers so the ones that change least are cached first.
- Never bake a secret into a layer; it stays in the history even if deleted later.
- Run as a non-root user unless there is a stated reason.
- Pin base image tags to a digest for reproducibility.

## Pitfalls

- A build context including the whole repository because .dockerignore is missing.
- A process running as PID 1 that ignores signals, so stop takes ten seconds.
- Bind mounts hiding the fact that the image itself is broken.

## Verify

Build clean, without cache, and run the container as it will actually run.
