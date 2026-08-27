---
id: function.observability
kind: function
title: Observability
summary: Being able to answer a question about production without a deploy.
task_phrases:
  - 'logging'
  - 'observability'
  - 'metrics'
  - 'tracing'
  - 'telemetry'
  - 'monitoring'
---

## Cares about

Whether the running system can be asked what it is doing.

## Working rules

- Instrument the question you will actually ask, not everything measurable.
- Structured events over free text. A log nobody can query is a log nobody reads.
- Correlate: one identifier that follows a request across every component.
- Never log a secret, a token or personal data.

## Pitfalls

- Cardinality explosion from putting an identifier in a metric label.
- Logging inside a hot loop and changing the timing.
- Alerting on a cause rather than on the symptom users feel.

## Verify

Ask the question you built it for, against real data.
