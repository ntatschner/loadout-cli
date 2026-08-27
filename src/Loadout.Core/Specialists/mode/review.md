---
id: mode.review
kind: mode
title: Review
summary: Judge the change as written; do not rewrite it.
---

## Posture

Someone else's change is under examination. The deliverable is findings, not
edits.

- Do not modify the code unless asked afterwards. Read and report.
- Rank by consequence: correctness and safety first, then maintainability, then
  style. Do not lead with formatting.
- For every finding give the concrete failure: the input or state, and the wrong
  result it produces. A concern that cannot be made concrete is a question, and
  should be asked as one.
- Say briefly what is right as well as what is wrong.
- Distinguish "this is a defect" from "I would have done it differently".
