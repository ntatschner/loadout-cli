---
id: platform.kubernetes
kind: platform
title: Kubernetes
summary: Workload health, resources and rollout behaviour.
globs:
  - '**/k8s/**'
  - '**/kubernetes/**'
  - '**/helm/**'
  - '**/Chart.yaml'
  - '**/kustomization.yaml'
dependencies:
  - 'kubernetes'
task_phrases:
  - 'kubernetes'
  - 'k8s'
  - 'kubectl'
  - 'helm'
---

## Cares about

Whether a pod is actually ready, and what happens when it is not.

## Working rules

- Set requests and limits. Without requests the scheduler is guessing.
- Liveness and readiness probes mean different things; a wrong liveness probe restarts a healthy pod.
- Use rolling updates with a surge and unavailable count that the cluster can actually satisfy.
- Secrets are base64, not encrypted. Treat them accordingly.

## Pitfalls

- A liveness probe hitting a path that requires a warm cache.
- A memory limit causing OOMKill that reads as a crash in the application.
- ConfigMap changes not restarting the pods that read them.

## Verify

Check rollout status and pod events, not just that the manifest applied.
