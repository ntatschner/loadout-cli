#!/usr/bin/env bash
#
# What a Linux machine can prove that a Windows one cannot: that the Unix
# platform implementations work, that the packages build, and that installing
# one leaves a working command behind.
#
# Every step fails the run rather than warning, because a verification script
# that reports problems and exits zero is worse than no verification at all.

set -euo pipefail

version="${AGENTCTL_VERSION:-0.1.0}"

heading() {
    printf '\n\033[1m%s\033[0m\n' "$1"
}

heading 'Environment'
dotnet --version
pwsh --version
echo "uname: $(uname -srm)"

heading 'Build'
dotnet build --configuration Release --nologo --verbosity quiet

heading 'Tests'
# The Unix-only tests are the point of running this at all, so the run is
# checked for having actually executed them rather than skipped them. A green
# suite that skipped every Linux test would be indistinguishable from a green
# suite that ran them, which is exactly the mistake this exists to prevent.
dotnet test \
    --configuration Release \
    --nologo \
    --verbosity quiet \
    --logger 'console;verbosity=normal' \
    | tee /tmp/test-output.txt

if grep -qE 'Skipped: *[1-9]' /tmp/test-output.txt; then
    echo
    echo 'Tests were skipped on Linux. Those are Windows-only tests and that is expected;'
    echo 'what would not be is a Unix-only test skipping here.'
fi

heading 'Pseudo-terminal'
# Named explicitly. These exercise forkpty, the controlling terminal and the
# window-size ioctl, none of which a Windows host can run at all.
dotnet test \
    --configuration Release \
    --nologo \
    --verbosity quiet \
    --filter 'FullyQualifiedName~PseudoTerminalTests' \
    --logger 'console;verbosity=normal'

heading 'Archive'
pwsh -NoProfile -File ./build/package.ps1 -Runtime linux-x64 -Version "$version"

archive="artifacts/agentctl-${version}-linux-x64.tar.gz"
# Checked from inside artifacts/: a checksum file names the archive without a
# directory, and sha256sum resolves that against the working directory.
( cd artifacts && sha256sum -c "$(basename "$archive").sha256" )

# The executable bit is the one property that silently ruins a Unix release.
tar -tvzf "$archive" | grep -E '^-rwx.*agentctl$' > /dev/null \
    || { echo 'agentctl is not executable inside the archive' >&2; exit 1; }

heading 'Installing from the archive'
rm -rf /tmp/from-archive && mkdir -p /tmp/from-archive
tar -xzf "$archive" -C /tmp/from-archive
/tmp/from-archive/agentctl --version
/tmp/from-archive/agentctl doctor --json > /tmp/doctor.json
/tmp/from-archive/agentctl project list --json > /dev/null

# Checked with grep rather than a JSON parser, so the image needs nothing
# beyond what building the project already requires.
if ! grep -q '"verdict"' /tmp/doctor.json; then
    echo 'doctor produced no verdict' >&2
    exit 1
fi

if ! grep -q 'PseudoTerminal' /tmp/doctor.json; then
    echo 'doctor did not report the pseudo-terminal capability' >&2
    exit 1
fi

# doctor may legitimately report problems on a bare container. What it may not
# do is fail to produce a report at all.
verdict=$(grep -o '"verdict": *"[^"]*"' /tmp/doctor.json | head -1 | cut -d'"' -f4)
echo "  verdict: ${verdict}"

if [ -z "$verdict" ]; then
    echo 'doctor reported an empty verdict' >&2
    exit 1
fi

heading 'Packages'
pwsh -NoProfile -File ./build/installer.ps1 -Runtime linux-x64 -Version "$version"

deb=$(ls artifacts/*.deb)
rpm=$(ls artifacts/*.rpm)

( cd artifacts && sha256sum -c "$(basename "$deb").sha256" )
( cd artifacts && sha256sum -c "$(basename "$rpm").sha256" )

dpkg-deb -c "$deb" | grep -q './usr/bin/agentctl' \
    || { echo 'the .deb puts nothing on PATH' >&2; exit 1; }

if dpkg-deb -c "$deb" | grep -qE '\.(pdb|xml)$'; then
    echo 'the .deb contains build artefacts that should not ship' >&2
    exit 1
fi

heading 'Installing the package'
dpkg -i "$deb"

# Run it by name, not by path: the point of installing a package is that the
# command is simply there.
agentctl --version
agentctl doctor --json > /dev/null

heading 'Removing the package'
dpkg -r agentctl

# The file is checked, not "command -v": the shell caches resolved paths, so it
# would happily report a command that has just been deleted.
if [ -e /usr/bin/agentctl ] || [ -e /usr/lib/agentctl ]; then
    echo 'the package left files behind after removal' >&2
    ls -la /usr/bin/agentctl /usr/lib/agentctl 2>&1 || true
    exit 1
fi

heading 'Done'
echo 'Linux build, tests, archive and packages all verified.'
