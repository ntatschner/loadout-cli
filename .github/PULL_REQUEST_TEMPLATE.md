## What this changes, and why

<!-- What was wrong or missing, and what the change does about it. If a
     decision has a cost as well as a benefit, say so here rather than leaving
     a reviewer to find it. -->

## How you know it works

<!-- What you ran, and what it said. "Tests pass" is worth less than the
     failing case you reproduced first and then didn't. -->

## Checklist

- [ ] `dotnet build Loadout.slnx` is silent (warnings are errors)
- [ ] `dotnet test tests/Loadout.Tests/Loadout.Tests.csproj` passes
- [ ] No OS-suffixed target framework, and shared code still uses only `Loadout.Platform.Abstractions`
- [ ] Anything unsupported on a platform is reported as a capability, not skipped quietly
- [ ] No secret value can reach stdout, logs, an exception message or `--json`
- [ ] Package changes include the updated `packages.lock.json` files
