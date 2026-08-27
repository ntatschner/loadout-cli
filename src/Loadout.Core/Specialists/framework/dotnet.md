---
id: framework.dotnet
kind: framework
title: .NET
summary: Host lifetime, dependency injection and configuration in .NET.
globs:
  - '**/*.csproj'
  - '**/*.sln'
  - '**/global.json'
dependencies:
  - 'Microsoft.Extensions.'
  - 'Microsoft.NET.Sdk'
task_phrases:
  - '.net'
  - 'dotnet'
requires:
  - 'language.csharp'
---

## Cares about

Object lifetime and where configuration actually comes from. Does not repeat the
C# language guidance.

## Working rules

- Match the registered lifetime to the dependency. A scoped service captured by
  a singleton is a bug that appears under load.
- Read configuration through the options pattern rather than reaching for the
  configuration root at call sites.
- Use `IHostedService` for background work so shutdown is orderly.
- Respect the target framework already in the project file.

## Pitfalls

- `IDisposable` singletons never disposed because nothing owns them.
- Configuration bound once at startup and expected to reload.
- `HttpClient` created per call, exhausting sockets.

## Verify

Run the tests; for lifetime problems, exercise more than one scope.
