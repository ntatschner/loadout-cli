# Installing

*Want the steps in order? [Installing, step by step](guides/installing.md) walks through it, and [installing in plain words](plain/installing.md) starts from opening a terminal.*

One command, which works out your platform and fetches the matching build:

```bash
curl -fsSL https://github.com/ntatschner/loadout-cli/releases/latest/download/install.sh | sh
loadout setup
```

```powershell
irm https://github.com/ntatschner/loadout-cli/releases/latest/download/install.ps1 | iex
loadout setup
```

`install.sh` picks the archive for your operating system and processor,
including the Apple silicon build from a shell running under Rosetta. It checks
the archive against the release's `SHA256SUMS` and refuses if the hash doesn't
match or the manifest doesn't list it. It installs the binary and the native
library that ships beside it into `~/.local/bin`, because that's where the
binary looks for the library, and needs no root. It refuses on a musl system
such as Alpine rather than installing a binary that can't start there, because
the Linux builds need glibc.

`install.ps1` does the same with the MSI, and checks its Authenticode signature
too. The checksum comes from the same place as the file, so it proves the
download arrived intact, not who built it; the signature is what says that. It
installs per user with no elevation, and adds the install directory to the
`PATH` of the terminal it ran in, so `loadout` works straight away. It runs in
Windows PowerShell 5.1 as well as PowerShell 7.

The macOS builds aren't signed or notarised yet. That doesn't matter here:
macOS quarantines files downloaded through a browser, and `curl` doesn't set
that flag, so Gatekeeper never looks at them. If you install an archive you
downloaded through a browser, `install.sh` clears the quarantine attribute from
the files it installed and nothing else. Nothing here will ever tell you to
turn Gatekeeper off; spec section 85 forbids it.

## Options

Pass options to `install.sh` after `sh -s --`, and to `install.ps1` by running
the downloaded file:

```bash
curl -fsSL .../install.sh | sh -s -- --version 0.111.0     # a particular release
curl -fsSL .../install.sh | sh -s -- --prefix /usr/local  # somewhere other than ~/.local
curl -fsSL .../install.sh | sh -s -- --dry-run            # download and verify, install nothing
sh install.sh --archive loadout-0.111.0-linux-x64.tar.gz   # an archive you already have
```

```powershell
./install.ps1 -Version 0.111.0
./install.ps1 -WhatIf          # download and verify, install nothing
```

Both take `--base-url` (`-BaseUrl`), a URL or a directory holding a copy of a
release's assets, for installing from a mirror or a file share. The mirror has
to carry `SHA256SUMS`, and `feed.json` too unless you name a version.

On Windows you can also extract the zip and put `loadout.exe` somewhere on
`PATH`.

## Native installers

A release also carries an `.msi`, a `.deb` and an `.rpm`, if you'd rather not
manage a `PATH` entry by hand:

```powershell
msiexec /i loadout-0.111.0-win-x64.msi        # per-user, no elevation
```

```bash
sudo dpkg -i loadout_0.111.0_amd64.deb        # or: sudo rpm -i loadout-0.111.0-1.x86_64.rpm
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

## Homebrew

On macOS or Linux, Homebrew is the shortest route:

```bash
brew install thecodesaiyan/loadout/loadout
loadout setup
```

The formula lives in
[TheCodeSaiyan/homebrew-loadout](https://github.com/TheCodeSaiyan/homebrew-loadout),
and the release workflow rewrites it on every published release from the
release's own `SHA256SUMS`. So Homebrew downloads the same archive the release
page offers, and checks it against the same hash. It installs the whole archive
rather than just the binary, because the native library shipped beside
`loadout` has to come with it.

Homebrew only learns about a release when it refreshes its copy of the tap.
`brew update` does that, and so does `brew upgrade` unless you've turned
auto-update off. Until then a release that has shipped looks as if it hasn't,
and that has been mistaken for a broken release workflow before.

Update it with Homebrew too:

```bash
brew upgrade loadout
```

Not `loadout update`. That doesn't know Homebrew put it there, so it would swap
the binary inside Homebrew's own directory while Homebrew went on believing it
had the older version.

## Building a release locally

```bash
pwsh ./build/package.ps1 -Runtime linux-x64 -Version 0.111.0     # archive
pwsh ./build/installer.ps1 -Runtime win-x64 -Version 0.111.0     # native installer
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
pwsh ./build/package.ps1 -Runtime osx-arm64 -Version 0.111.0
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

If you installed with Homebrew, use `brew upgrade loadout` instead;
[the Homebrew section](#homebrew) says why.

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

The launcher looks on its own as well, once a day and in the background, and
says so in its top-right corner when there is something newer — `v1.2.0 ·
1.3.0 available` beside the version it is running. It never waits on that
lookup to open and never reports one that failed; `off` silences it along with
`loadout update`. Installing is still yours to do.

The source is any JSON document you can reach over HTTP, or a path. A directory
on a share makes a perfectly good internal release source (spec section 79), and
nothing has to be running to answer:

```json
{
  "schemaVersion": 1,
  "version": "0.111.0",
  "notes": "What changed.",
  "artifacts": {
    "osx-arm64": {
      "url": "https://internal.example/loadout/loadout-0.111.0-osx-arm64.tar.gz",
      "sha256": "985daa42...",
      "size": 31110221
    }
  }
}
```

A feed can also list `installers`, keyed the same way, with the MSI for each
Windows platform. A copy the MSI installed is updated from that; a feed
without one tells such a copy to download the MSI and run it by hand.

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

### With the daemon running

A plain `loadout update` replaces the binary and leaves a running daemon
serving the old version until you restart it. `--restart-daemon` hands the
install to the daemon instead, which is what Studio's **Install and restart**
runs:

```bash
loadout update --restart-daemon                 # once the runs it started have finished
loadout update --restart-daemon --now           # straight away, ending them
loadout update --roll-back --restart-daemon     # back to the version before
```

The download is checked here, before anything is asked of the daemon, so a bad
one is reported while the daemon is still up to show it. The daemon then
finishes its runs, starting nothing new meanwhile, and hands over to a
short-lived helper. The helper runs from a copy outside the install folder,
because the daemon can't replace itself while it runs, and on a machine with
the Restart Manager off the MSI can't replace a running daemon either. It puts
the new version in place, starts the daemon and waits for it to say it's
serving. Each step goes in the daemon's log.

- **A copy unpacked from an archive** keeps the old binary as
  `loadout.previous`. If the new daemon doesn't start, the helper puts the old
  one back and starts that. `--roll-back` puts it back on purpose, and discards
  the newer binary rather than keeping it, so rolling back twice doesn't roll
  forward again.
- **A copy the MSI installed** is updated by running the newer MSI, so Windows
  goes on knowing which version is installed. winget installs the same MSI, so
  its copies are updated the same way rather than through `winget upgrade`,
  which is submitted once a day and can run behind the release. There's no
  previous binary, so no roll back, and a failed install is reported only in
  the daemon's log, with the MSI's own log beside the download. A Loadout
  session in progress stops the MSI, as it does when you run it by hand.

A copy counts as the MSI's when it's running from the folder the MSI recorded
under `HKCU\Software\Loadout\InstalledPath`. Anything else is an archive copy,
including a second copy elsewhere on a machine that also has the MSI.
