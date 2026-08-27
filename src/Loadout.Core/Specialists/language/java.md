---
id: language.java
kind: language
title: Java and Kotlin
summary: Nullability, resource lifetime and API compatibility on the JVM.
globs:
  - '**/*.java'
  - '**/*.kt'
  - '**/pom.xml'
  - '**/build.gradle'
  - '**/build.gradle.kts'
task_phrases:
  - 'java'
  - 'kotlin'
  - 'jvm'
  - 'gradle'
  - 'maven'
---

## Cares about

Null, resource cleanup, and what breaks a caller that was compiled against the
old signature.

## Working rules

- Use try-with-resources for anything closeable.
- Prefer immutability; expose collections as unmodifiable views.
- In Kotlin, keep platform types out of public signatures.
- Do not catch `Exception` to hide a specific failure you have not handled.

## Pitfalls

- `equals` without `hashCode`.
- Mutable static state shared across threads.
- Autoboxing in a hot loop.
- Adding a method to a published interface without a default.

## Verify

Run the module's tests. For public API changes, check binary compatibility, not
just source compatibility.

## Defer to

`function.backward-compatibility` for published API changes;
`function.concurrency` for shared state.
