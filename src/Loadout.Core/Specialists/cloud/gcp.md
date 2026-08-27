---
id: cloud.gcp
kind: cloud
title: Google Cloud
summary: Project scoping, IAM and service behaviour on GCP.
dependencies:
  - 'google-cloud'
  - 'google.cloud'
  - 'Google.Cloud'
task_phrases:
  - 'gcp'
  - 'google cloud'
  - 'bigquery'
  - 'cloud run'
---

## Cares about

Project boundaries and service account permissions.

## Working rules

- Use service accounts with the narrowest role; avoid primitive roles entirely.
- Be explicit about project and location on every resource.
- Enable only the APIs actually needed; enablement is itself a change.
- Prefer workload identity over downloaded key files.

## Pitfalls

- A downloaded service account key committed or left on disk.
- Quota applied per project rather than per resource, surprising under load.
- Regional versus multi-regional storage chosen by default rather than deliberately.

## Verify

Verify as the service account, not as your user.
