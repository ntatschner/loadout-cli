---
id: skill.architecture-review
kind: skill
title: Architecture review
summary: A procedure for evaluating a design against what it must do.
task_phrases:
  - 'architecture review'
  - 'review the design'
  - 'is this the right approach'
  - 'design review'
modes:
  - 'review'
  - 'advise'
  - 'investigate'
---

## When to use

A design or significant structural change needs assessment.

## Procedure

1. State what the design must achieve, and the constraints it must respect.
2. Identify the one-way decisions: what would be expensive to reverse?
3. Trace one concrete change the design is meant to make cheap, end to end.
4. Trace one concrete failure: what happens when each component is unavailable?
5. Identify the coupling: what must change together?
6. Ask what the design gives up. If nothing, it has not been understood.
7. Compare against the simplest thing that could work, and say why the extra is earned.
8. Report: what is sound, what is risky, and which decision to revisit first.

## Done when

- One-way decisions identified.
- A concrete change and a concrete failure both traced.
- The trade-off stated explicitly.
