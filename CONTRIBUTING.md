# Contributing

Thanks for looking. This is a small project with some deliberate rules, most of
which exist because something went wrong once.

## Getting a build

You need the .NET SDK **10.0.303 exactly**. `global.json` pins it and
`rollForward` is off, so a different 10.x will refuse rather than quietly build
you something else.

```sh
dotnet restore --locked-mode
dotnet build Loadout.slnx
dotnet test tests/Loadout.Tests/Loadout.Tests.csproj
```

Warnings are errors. The build should be silent.

## Dependencies

Package versions live in `Directory.Packages.props`, pinned exactly, with a
`packages.lock.json` per project. Nothing floats, so the same commit builds the
same binaries in a year's time, which matters because releases ship signed
installers.

Changing a package means editing `Directory.Packages.props`, running a plain
`dotnet restore`, and committing the changed lock files with the rest of the
work. If CI complains that the lock file is inconsistent, that is the check
doing its job: you have a dependency change that is not in the commit.

## Style

`.editorconfig` has it, and it describes what the code already does rather than
an opinion imposed on it: file-scoped namespaces, braces on their own line,
four spaces, `_camelCase` private fields, PascalCase constants including local
ones. `dotnet format` will apply it.

It is deliberately not enforced at build time. Warnings are errors here and a
formatting preference has no business failing a release.

## Things that will get a change sent back

**An OS-suffixed target framework.** `net10.0-windows` compiles happily on a
Windows machine and breaks Linux and macOS outright. There is a test that fails
the build if one appears.

**Shared code reaching into a platform implementation.** `Loadout.Core` and
everything above it may use `Loadout.Platform.Abstractions` and nothing else. A
second test reads the compiled assemblies and fails if `Loadout.Platform.Windows`
or its siblings turn up where they should not.

**A feature that only works on one platform, quietly.** If something genuinely
cannot be done somewhere, report it as an unsupported capability so
`loadout doctor` can show it. Silently doing nothing is the thing being
avoided.

**Printing a secret.** Detection reports the pattern that matched and never the
value, anywhere: not to stdout, not to logs, not in an exception message, not
in `--json`. This is not negotiable and there is no debug flag that relaxes it.

**Rewriting somebody's Git history, or reaching the network without being
asked.** Neither is something a launcher should do on your behalf.

## Tests

They are split by what they need, and the split matters:

| Folder | What it is for |
| --- | --- |
| `Unit` | No filesystem, no processes, fast |
| `Integration` | Real repositories on disk, the real `git` binary |
| `Contract` | Spawns the built `loadout` executable and treats it as a stranger would |
| `Platform` | Behaviour that genuinely differs per OS |
| `Architecture` | The structural rules above |

Contract tests run the real binary, so rebuilding while they run will fail them
for reasons that have nothing to do with your change.

A test that fails only in the full suite is worth two minutes of thought before
you re-run it. It is usually a disturbed binary. It is sometimes a race, and
those look identical from the outside.

## Commits

Write the message for somebody reading it in a year with no memory of the
conversation. Say what changed and why it was worth changing, and where a
decision has a cost, say what the cost is rather than only the benefit.
