#!/bin/sh
# Installs loadout on Linux or macOS from a release archive.
#
# Spec sections 19 and 20 both document a direct-binary install, and both are
# explicit that root must not be required for ordinary use. This installs to
# ~/.local/bin by default and only touches a system directory if asked.
#
# Usage:
#   ./install.sh                          install from a sibling tar.gz
#   ./install.sh --archive path.tar.gz    install a named archive
#   ./install.sh --prefix /usr/local      install system-wide (needs write access)

set -eu

PREFIX="${HOME}/.local"
ARCHIVE=""

while [ $# -gt 0 ]; do
    case "$1" in
        --prefix)
            PREFIX="$2"
            shift 2
            ;;
        --archive)
            ARCHIVE="$2"
            shift 2
            ;;
        -h|--help)
            sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 2
            ;;
    esac
done

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if [ -z "$ARCHIVE" ]; then
    # Pick the newest matching archive beside this script, so extracting a
    # release and running ./install.sh just works.
    ARCHIVE=$(ls -t "${script_directory}"/loadout-*.tar.gz 2>/dev/null | head -1 || true)
fi

if [ -z "$ARCHIVE" ] || [ ! -f "$ARCHIVE" ]; then
    echo "No archive found. Pass one with --archive." >&2
    exit 1
fi

# Verify the download before running anything out of it. The checksum file is
# optional because someone may have built the archive themselves, but when it is
# present a mismatch stops the install rather than being mentioned in passing.
if [ -f "${ARCHIVE}.sha256" ]; then
    if command -v sha256sum >/dev/null 2>&1; then
        checker="sha256sum -c"
    elif command -v shasum >/dev/null 2>&1; then
        checker="shasum -a 256 -c"
    else
        checker=""
    fi

    if [ -n "$checker" ]; then
        archive_directory=$(CDPATH= cd -- "$(dirname -- "$ARCHIVE")" && pwd)

        if ! (cd "$archive_directory" && $checker "$(basename "${ARCHIVE}.sha256")" >/dev/null 2>&1); then
            echo "Checksum mismatch for ${ARCHIVE}. Not installing." >&2
            exit 1
        fi

        echo "Checksum verified."
    else
        echo "No sha256 tool found; skipping verification." >&2
    fi
fi

BIN_DIR="${PREFIX}/bin"
mkdir -p "$BIN_DIR"

temporary=$(mktemp -d)
# shellcheck disable=SC2064
trap "rm -rf '${temporary}'" EXIT INT TERM

tar -xzf "$ARCHIVE" -C "$temporary"

if [ ! -f "${temporary}/loadout" ]; then
    echo "The archive does not contain loadout." >&2
    exit 1
fi

# Installed with an explicit mode rather than a plain copy: an archive built on
# a Windows machine can arrive without the executable bit, and a binary that
# will not run is a confusing way to finish an install.
install -m 0755 "${temporary}/loadout" "${BIN_DIR}/loadout"

echo "Installed ${BIN_DIR}/loadout"

# macOS quarantines anything downloaded through a browser. Until the binary is
# signed and notarised, Gatekeeper blocks it, and the honest fix is to remove
# the attribute from the files this installed rather than to tell people to
# disable Gatekeeper, which spec section 85 forbids.
clear_quarantine() {
    if [ "$(uname -s)" = "Darwin" ] && command -v xattr >/dev/null 2>&1; then
        if xattr -p com.apple.quarantine "$1" >/dev/null 2>&1; then
            xattr -d com.apple.quarantine "$1" 2>/dev/null || true
            echo "Removed the download quarantine attribute from $(basename -- "$1")."
        fi
    fi
}

clear_quarantine "${BIN_DIR}/loadout"

# The archive carries native libraries beside the binary — libonigwrap.dylib
# on macOS, libonigwrap.so on Linux — which the single-file bundle does not
# contain. The runtime looks for them in the executable's own directory, so
# they go into the same one. Installing the binary alone left them behind, the
# mistake the Homebrew formula's install block was written to avoid.
for library in "${temporary}"/lib*.dylib "${temporary}"/lib*.so; do
    [ -f "$library" ] || continue
    install -m 0755 "$library" "${BIN_DIR}/$(basename -- "$library")"
    echo "Installed ${BIN_DIR}/$(basename -- "$library")"
    clear_quarantine "${BIN_DIR}/$(basename -- "$library")"
done

case ":${PATH}:" in
    *":${BIN_DIR}:"*)
        ;;
    *)
        echo
        echo "${BIN_DIR} is not on your PATH. Add it with:"
        echo
        echo "  echo 'export PATH=\"${BIN_DIR}:\$PATH\"' >> ~/.profile"
        echo
        ;;
esac

echo
echo "Next: loadout setup"
