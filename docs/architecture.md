# Architecture and building

```text
Loadout.Models      Records, DTOs, config classes. No logic.
Loadout.Platform    Abstractions + Windows / Linux / macOS / Unix implementations.
Loadout.Core        Projects, Git, Workspace, Configuration, Security, Diagnostics.
Loadout.Agents      Claude, Codex and generic adapters; the launch pipeline.
Loadout.Cli         The loadout executable.
Loadout.Tui         The full-screen launcher, and the first-run questions.
```

The rule that makes cross-platform parity hold is that **`Core`, `Agents` and
`Tui` depend on `Platform.Abstractions` only**. Exactly one file —
[`PlatformServices.cs`](../src/Loadout.Platform/PlatformServices.cs) —
branches on the operating system. Two tests in
[`ArchitectureTests.cs`](../tests/Loadout.Tests/Architecture/ArchitectureTests.cs)
enforce this: one reads each assembly's type-reference table to prove no shared
assembly touches a platform implementation, the other proves no project carries
an OS-suffixed target framework.

### Where things are stored

| | Windows | Linux | macOS |
|---|---|---|---|
| Config | `%APPDATA%\Loadout` | `$XDG_CONFIG_HOME/loadout` | `~/Library/Application Support/Loadout` |
| State | `%LOCALAPPDATA%\Loadout` | `$XDG_DATA_HOME/loadout` | `…/Application Support/Loadout/state` |
| Cache | `…\cache` | `$XDG_CACHE_HOME/loadout` | `~/Library/Caches/Loadout/cache` |
| Logs | `…\logs` | `$XDG_STATE_HOME/loadout/logs` | `~/Library/Logs/Loadout` |
| Secrets | Credential Manager | Secret Service (libsecret) | Keychain |

macOS uses native conventions by default. Set `LOADOUT_USE_XDG=1` to place
launcher files under the XDG roots instead.

`config.yaml` is portable user preference. `machines.yaml` holds this machine's
absolute paths and never leaves it — the same project definition works unchanged
on a Windows desktop, a Linux workstation and a Mac.

### Capabilities, not silent gaps

Anything a platform cannot do is reported rather than quietly skipped. Run
`loadout doctor` to see the full matrix; each unavailable capability carries
the reason. Known gaps today:

- **Pseudo-terminal window size on macOS** — the launcher owns a real PTY on
  every platform, but on macOS it cannot set that terminal's size. `ioctl` is
  variadic there, and on Apple Silicon a variadic argument is passed on the
  stack while a fixed-signature P/Invoke passes it in a register, so the size
  never reaches the kernel: the call reports success and the child reads a
  size that was never sent. The session works; only the dimensions the agent
  is told about are wrong, so anything drawing a table or a progress bar
  measures the wrong width.
- **macOS desktop integration** — `loadout desktop` installs a Start Menu
  shortcut on Windows and a `.desktop` entry on Linux. On macOS the `.app`
  bundle is not built yet, so the command says so and declines. Every feature
  stays reachable from the CLI and TUI.

## Build and run

```bash
dotnet build
dotnet run --project src/Loadout.Cli -- doctor
dotnet test
```

Publish a self-contained binary:

```bash
dotnet publish src/Loadout.Cli -c Release -r osx-arm64 --self-contained
```

Supported runtime identifiers: `win-x64`, `win-arm64`, `linux-x64`,
`linux-arm64`, `osx-x64`, `osx-arm64`.

## Testing

The suite covers more than units. Four kinds are worth knowing about, because
each exists for a class of defect that reached a user:

- **Contract tests** run the built command line as a real process against a
  throwaway home and pin the shape of every `--json` document. Renaming a
  published property fails them. `--json` is what scripts read, and nothing
  asserted any of it before.
- **Interaction tests** drive the launcher with keystrokes on a headless ANSI
  driver, at 80×24 through 200×60, and assert on what was actually drawn.
  Building screens without pressing keys let three defects through: a crash on
  startup, a menu naming a command that did not exist, and a capability that
  vanished in a rewrite.
- **Leakage tests** plant a synthetic credential and search every output path
  for it — stdout, stderr, JSON, and a full stack trace under `--debug`.
- **Mutation checks** are how the tests above were trusted: each was confirmed
  by breaking the thing it covers and watching it fail.

```bash
dotnet test
```

The suite is deliberately structured so most of it runs everywhere:

- **Shared acceptance tests** exercise registration, resolution, discovery and
  Git against real repositories, with identical assertions on all three
  platforms.
- **Path layout tests** verify the Windows, Linux and macOS layouts from *any*
  host by injecting the environment, so no layout is left unverified on a given
  CI leg.
- **Platform tests** (Credential Manager, Unix mode bits) skip rather than
  silently pass off their platform, so the run summary shows what did not apply.

## Code signing

Windows binaries and installers are signed with Azure Trusted Signing under a
certificate issued to TheCodeSaiyan Ltd. Both the executable and the installer
around it are signed: the installer's signature is what Windows checks when the
`.msi` is opened, and the executable's is what it checks afterwards, every time
the installed command runs.

There is no private key on any build machine. The certificate stays in Azure,
`signtool` reaches it through Microsoft's signing library, and the build
authenticates with a short-lived OIDC token exchanged by `azure/login` — so
there is no long-lived credential to store, leak or rotate.

Signing is driven entirely by environment, and the switch is the presence of
`ARTIFACT_SIGNING_ACCOUNT`:

| State | What happens |
|---|---|
| Unset | Builds unsigned, with a notice. This is a developer machine. |
| Set, others missing | Refuses to build, rather than silently shipping unsigned. |
| Fully set | Signs and then verifies each file. |

`build/sign-windows.ps1` holds the whole of it, and `package.ps1` and
`installer.ps1` call it at the two points that matter. A local build takes the
same path and simply produces an unsigned binary, so the signed and unsigned
builds differ in one input rather than in which script ran.

## Verifying the Linux build without Linux

Everything below the platform seam is untestable from the host it was not
written for, and "it compiles" is not the same claim as "it works". The Unix
pseudo-terminal in particular allocates a tty, spawns into it and drives a real
session; none of that is exercised by building it.

```powershell
pwsh ./build/verify-linux.ps1                      # linux-x64
pwsh ./build/verify-linux.ps1 -Architecture arm64  # linux-arm64, emulated
```

That builds a container, runs the whole suite there, packages the archive, the
`.deb` and the `.rpm`, installs the package, runs the installed command by name
and removes it again. It is a development convenience only — spec section 1
forbids a container from being any part of how the launcher runs, and CI still
runs these tests natively on its Ubuntu leg.

It earns its keep. Running it the first time found four defects that a Windows
machine cannot see: a `waitpid` call that reaped unrelated child processes, a
library that resolves under a name only present with development packages
installed, a pre-commit hook test that proved nothing because a fake stood in
for the executable bit, and an assertion about Windows paths that could only
ever pass on Windows.

The `arm64` run is emulated, which is slow but is the only way to execute a
`linux-arm64` build without arm64 hardware — that build is otherwise
cross-compiled and never run anywhere. It found a fifth defect: `posix_spawn`
reports a missing executable to the caller on x64 and lets the child exit 127 on
arm64, so the same missing agent would have produced a clear error on one
machine and silence on another. The launcher now checks before it spawns, which
also matches what Windows already did.

Emulation cannot build Debian or RPM packages: the `stat` that `tar
--no-recursion` depends on returns `EINVAL` under QEMU, and a two-file package
built by hand fails the same way. The script probes for that with a throwaway
package and skips the step with a reason rather than reporting a defect that is
not there. Packages for arm64 are built on an x86-64 host in CI, where `tar`
behaves; installing an arm64 package *on* arm64 is the one thing still covered
nowhere.

