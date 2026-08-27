# Installing

Download the archive for your platform, verify it, and install:

```bash
tar -xzf loadout-0.9.2-linux-x64.tar.gz
./install.sh                       # installs to ~/.local/bin, no root needed
loadout setup
```

`install.sh` verifies the SHA-256 before extracting anything and refuses to
install on a mismatch. On macOS it also clears the download quarantine
attribute from the installed binary — until the binary is signed and notarised
Gatekeeper would otherwise block it, and clearing the attribute on one file is
the honest fix. The documentation never tells anyone to disable Gatekeeper,
which spec section 85 forbids.

On Windows, extract the zip and put `loadout.exe` somewhere on `PATH`.

### Native installers

A release also carries an `.msi`, a `.deb` and an `.rpm` for people who would
rather not manage a `PATH` entry by hand:

```powershell
msiexec /i loadout-0.9.2-win-x64.msi        # per-user, no elevation
```

```bash
sudo dpkg -i loadout_0.9.2_amd64.deb        # or: sudo rpm -i loadout-0.9.2-1.x86_64.rpm
```

The MSI installs per user into `%LOCALAPPDATA%\Programs\loadout`, adds that
directory to the user `PATH` and creates a Start Menu entry. It installs
somewhere other than the launcher's own data directory on purpose: they would
otherwise share a parent, and an uninstall that tidied up its install root a
little too enthusiastically would take the workspace clone and backup sets with
it. Uninstalling removes the binaries, the `PATH` entry and the shortcut, and
leaves everything under `%LOCALAPPDATA%\Loadout` alone.

The Linux packages put the self-contained build under `/usr/lib/loadout` with
a symlink at `/usr/bin/loadout`, rather than emptying a hundred-file publish
directory into `/usr/bin`.

macOS has archives only. A `.pkg` needs signing and notarisation to be
installable without the user fighting Gatekeeper, and that needs a Developer ID
and a Mac to verify it on; until both exist, shipping an unsigned installer
would be worse than shipping none.

### Building a release locally

```bash
pwsh ./build/package.ps1 -Runtime linux-x64 -Version 0.9.2     # archive
pwsh ./build/installer.ps1 -Runtime win-x64 -Version 0.9.2     # native installer
```

The installer script builds each format with the tooling that owns it — WiX for
the MSI, `dpkg-deb` and `rpmbuild` for the Linux packages — so it refuses to
build a Linux package on Windows rather than assembling the container format by
hand. A `.deb` written by an `ar` writer of our own would work right up until it
did not, and would then fail inside somebody else's package manager where the
error would make no sense to them.

The MSI needs WiX 5 (`dotnet tool install --global wix --version 5.0.2`). The
pin is deliberate: WiX 6 and later require accepting the Open Source Maintenance
Fee agreement, which is a decision for whoever owns this project rather than one
a build script should make on their behalf.

```bash
pwsh ./build/package.ps1 -Runtime osx-arm64 -Version 0.9.2
```

Produces the archive and its checksum in `artifacts/`. Unix archives are built
with the executable bit set even when packaged from Windows, where the
filesystem has no mode to preserve — without that the extracted binary would not
run. This needs the GNU `tar` that ships with Git; the `bsdtar` built into
Windows cannot set the bit and the script says so rather than producing a
quietly broken archive.

## Updating

```bash
loadout config set updates-source https://internal.example/loadout/feed.json
loadout update --check
loadout update
```

The source is any JSON document reachable over HTTP, or a path — a directory on
a share is a perfectly good internal release source (spec section 79), and no
service has to answer:

```json
{
  "schemaVersion": 1,
  "version": "0.9.2",
  "notes": "What changed.",
  "artifacts": {
    "osx-arm64": {
      "url": "https://internal.example/loadout/loadout-0.9.2-osx-arm64.tar.gz",
      "sha256": "985daa42...",
      "size": 31110221
    }
  }
}
```

Replacing the binary somebody is about to run is the most dangerous thing the
launcher does, so:

- **A published SHA-256 is required.** A feed that will not commit to a hash can
  hand over anything, and that download becomes the binary you run next. The
  update is refused with exit 9.
- **The hash is checked before anything is put in place**, and a mismatch leaves
  the working binary exactly where it was.
- **The previous binary is kept** as `loadout.previous`, so a bad update can be
  undone by hand rather than reinstalled.
- **Nothing updates without being asked.** `--yes` or a prompt; non-interactively
  it refuses rather than swapping the binary out from under a script.
- **A malformed or older version is never treated as newer**, so a rolled-back or
  broken feed cannot walk you backwards.

The running executable is moved aside rather than overwritten, because Windows
will not let a running image be replaced but will let it be renamed.

