# Installing

Download the archive for your platform, verify it, and install:

```bash
tar -xzf loadout-0.26.0-linux-x64.tar.gz
./install.sh                       # installs to ~/.local/bin, no root needed
loadout setup
```

`install.sh` checks the SHA-256 before it extracts anything, and refuses on a
mismatch. On macOS it also clears the download quarantine attribute from the
installed binary. The binary isn't signed or notarised yet, so Gatekeeper would
block it otherwise, and clearing the attribute on one file is the honest fix.
Nothing here will ever tell you to turn Gatekeeper off; spec section 85 forbids
it.

On Windows, extract the zip and put `loadout.exe` somewhere on `PATH`.

### Native installers

A release also carries an `.msi`, a `.deb` and an `.rpm`, if you'd rather not
manage a `PATH` entry by hand:

```powershell
msiexec /i loadout-0.26.0-win-x64.msi        # per-user, no elevation
```

```bash
sudo dpkg -i loadout_0.26.0_amd64.deb        # or: sudo rpm -i loadout-0.26.0-1.x86_64.rpm
```

The MSI installs per user into `%LOCALAPPDATA%\Programs\loadout`, adds that to
your `PATH` and makes a Start Menu entry. It deliberately doesn't install
next to the launcher's own data. If they shared a parent, one over-enthusiastic
uninstall would take your workspace clone and every backup set with it.
Uninstalling removes the binaries, the `PATH` entry and the shortcut, and leaves
everything under `%LOCALAPPDATA%\Loadout` alone.

The Linux packages put the self-contained build under `/usr/lib/loadout` and
symlink `/usr/bin/loadout` at it, rather than tipping a hundred-file publish
directory into `/usr/bin`.

macOS gets archives only. A `.pkg` has to be signed and notarised or you'll
spend the install fighting Gatekeeper, and that needs a Developer ID and a Mac
to verify it on. Until both exist, an unsigned installer would be worse than
none.

### Building a release locally

```bash
pwsh ./build/package.ps1 -Runtime linux-x64 -Version 0.26.0     # archive
pwsh ./build/installer.ps1 -Runtime win-x64 -Version 0.26.0     # native installer
```

Each format is built by the tooling that owns it — WiX for the MSI, `dpkg-deb`
and `rpmbuild` for the Linux packages. So the script refuses to build a Linux
package on Windows instead of assembling the container format itself. A `.deb`
written by our own `ar` writer would work right up until it didn't, and then
fail inside somebody else's package manager, where the error would mean nothing
to them.

The MSI needs WiX 5 (`dotnet tool install --global wix --version 5.0.2`). That
pin is on purpose. WiX 6 and later want you to accept the Open Source
Maintenance Fee agreement, and that's a decision for whoever owns the project,
not one a build script should make for them.

```bash
pwsh ./build/package.ps1 -Runtime osx-arm64 -Version 0.26.0
```

That leaves the archive and its checksum in `artifacts/`. Unix archives get the
executable bit set even when they're packaged from Windows, where the filesystem
has no mode to preserve — without it, the binary you extract won't run. This
needs the GNU `tar` that ships with Git. The `bsdtar` built into Windows can't
set the bit, and the script says so rather than handing you a quietly broken
archive.

## Updating

```bash
loadout update --check
loadout update
```

Out of the box that reads this project's own releases. Every release publishes a
`feed.json` beside the archives, and the setting stays empty until you say
otherwise. Empty means the default, so your machine follows the feed wherever it
moves instead of pinning the URL that happened to be current the day you set it
up.

First-run setup asks, and takes the answer as a flag when nobody's there:

```bash
loadout setup --no-update-feed                                   # never check
loadout setup --update-feed https://internal.example/feed.json   # somewhere else
```

After that it's one setting, shared with every machine on the workspace:

```bash
loadout config set updates-source https://internal.example/loadout/feed.json
loadout config set updates-source off       # never check
loadout config set updates-source ""        # back to this project's releases
```

The source is any JSON document you can reach over HTTP, or a path. A directory
on a share makes a perfectly good internal release source (spec section 79), and
nothing has to be running to answer:

```json
{
  "schemaVersion": 1,
  "version": "0.26.0",
  "notes": "What changed.",
  "artifacts": {
    "osx-arm64": {
      "url": "https://internal.example/loadout/loadout-0.26.0-osx-arm64.tar.gz",
      "sha256": "985daa42...",
      "size": 31110221
    }
  }
}
```

Replacing the binary you're about to run is the most dangerous thing the
launcher does. So:

- **A published SHA-256 is required.** A feed that won't commit to a hash can
  hand over anything, and that download becomes the binary you run next. The
  update is refused with exit 9.
- **The hash is checked before anything is put in place**, and a mismatch leaves
  the working binary exactly where it was.
- **The previous binary is kept** as `loadout.previous`, so you can undo a bad
  update by hand instead of reinstalling.
- **Nothing updates without being asked.** `--yes` or a prompt; with nobody
  there it refuses rather than swapping the binary out from under a script.
- **A malformed or older version is never treated as newer**, so a rolled-back
  or broken feed can't walk you backwards.

The running executable is moved aside rather than overwritten, because Windows
won't let a running image be replaced but will let it be renamed.
