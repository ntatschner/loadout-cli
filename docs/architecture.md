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

macOS uses native conventions by default. Set `LOADOUT_USE_XDG=1` to put
launcher files under the XDG roots instead.

`config.yaml` is portable user preference. `machines.yaml` holds this machine's
absolute paths and never leaves it, so the same project definition works
unchanged on a Windows desktop, a Linux workstation and a Mac.

### Capabilities, not silent gaps

Anything a platform can't do gets reported rather than quietly skipped. Run
`loadout doctor` for the full matrix; every unavailable capability comes with
the reason. What's missing today:

- **Pseudo-terminal window size on macOS** — the launcher owns a real PTY
  everywhere, but on macOS it can't set that terminal's size. `ioctl` is
  variadic there, and on Apple Silicon a variadic argument goes on the stack
  while a fixed-signature P/Invoke passes it in a register. So the size never
  reaches the kernel: the call reports success and the child reads a size
  nobody sent. The session itself works. Only the dimensions the agent is told
  about are wrong, so anything drawing a table or a progress bar measures the
  wrong width.
- **macOS desktop integration** — `loadout desktop` installs a Start Menu
  shortcut on Windows and a `.desktop` entry on Linux. The macOS `.app` bundle
  isn't built yet, so the command says so and declines. Everything stays
  reachable from the CLI and TUI.

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
each one exists for a class of defect the others can't reach:

- **Contract tests** run the built command line as a real process against a
  throwaway home, and pin the shape of every `--json` document. Rename a
  published property and they fail. `--json` is what scripts read, so its shape
  is a promise.
- **Interaction tests** drive the launcher with keystrokes on a headless ANSI
  driver, at 80×24 through 200×60, and assert on what actually got drawn.
  Building screens without pressing keys catches none of what this does: a
  crash on startup, a menu naming a command that doesn't exist, a capability
  that vanished in a rewrite.
- **Leakage tests** plant a synthetic credential and hunt for it down every
  output path — stdout, stderr, JSON, and a full stack trace under `--debug`.
- **Mutation checks** are why the tests above are worth trusting. Each one was
  confirmed by breaking the thing it covers and watching it fail.

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
  silently pass off their platform, so the run summary shows what didn't apply.

## Code signing

Windows binaries and installers are signed with Azure Trusted Signing, under a
certificate issued to TheCodeSaiyan Ltd. Both the executable and the installer
around it get signed. The installer's signature is what Windows checks when you
open the `.msi`; the executable's is what it checks afterwards, every time the
installed command runs.

No build machine holds a private key. The certificate stays in Azure, `signtool`
reaches it through Microsoft's signing library, and the build authenticates with
a short-lived OIDC token exchanged by `azure/login`. There's no long-lived
credential to store, leak or rotate.

Signing is driven entirely by environment, and the switch is the presence of
`ARTIFACT_SIGNING_ACCOUNT`:

| State | What happens |
|---|---|
| Unset | Builds unsigned, with a notice. This is a developer machine. |
| Set, others missing | Refuses to build, rather than silently shipping unsigned. |
| Fully set | Signs and then verifies each file. |

`build/sign-windows.ps1` holds all of it, and `package.ps1` and `installer.ps1`
call it at the two points that matter. A local build takes the same path and
just produces an unsigned binary, so signed and unsigned builds differ in one
input rather than in which script ran.

## Verifying the Linux build without Linux

Everything below the platform seam is untestable from a host it wasn't written
for, and "it compiles" isn't the same claim as "it works". The Unix
pseudo-terminal is the clearest case: it allocates a tty, spawns into it and
drives a real session, and building it exercises none of that.

```powershell
pwsh ./build/verify-linux.ps1                      # linux-x64
pwsh ./build/verify-linux.ps1 -Architecture arm64  # linux-arm64, emulated
```

That builds a container, runs the whole suite in it, packages the archive, the
`.deb` and the `.rpm`, installs the package, runs the installed command by name
and removes it again. It's a development convenience and nothing more — spec
section 1 forbids a container from being any part of how the launcher runs, and
CI still runs these tests natively on its Ubuntu leg.

It earns its keep, because it catches what a Windows machine can't see: a
`waitpid` call that reaps unrelated child processes, a library that only
resolves under a name you get with development packages installed, a pre-commit
hook test that proves nothing because a fake stands in for the executable bit,
an assertion about Windows paths that could only ever pass on Windows.

The `arm64` run is emulated. That's slow, and it's the only way to run a
`linux-arm64` build without arm64 hardware — otherwise that build is
cross-compiled and never runs anywhere. It's worth the wait for defects that
only appear there: `posix_spawn` reports a missing executable to the caller on
x64 and lets the child exit 127 on arm64, so the same missing agent gives a
clear error on one machine and silence on the other. The launcher checks before
it spawns, which is what Windows already did.

Emulation can't build Debian or RPM packages. The `stat` that `tar
--no-recursion` relies on returns `EINVAL` under QEMU, and a two-file package
built by hand fails the same way. The script probes for it with a throwaway
package and skips the step with a reason, rather than reporting a defect that
isn't there. arm64 packages are built on an x86-64 host in CI, where `tar`
behaves. Installing an arm64 package *on* arm64 is the one thing still covered
nowhere.

