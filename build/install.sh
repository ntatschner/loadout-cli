#!/bin/sh
# Installs loadout on Linux or macOS.
#
#   curl -fsSL https://github.com/ntatschner/loadout-cli/releases/latest/download/install.sh | sh
#
# Works out the platform, downloads that release archive, checks it against
# the release's SHA256SUMS, and installs to ~/.local/bin. Spec sections 19 and
# 20 are explicit that root must not be required for ordinary use, so a system
# directory is only touched if asked for.
#
# Options, passed after `sh -s --` when piping:
#   --version 0.50.0          install that release rather than the latest
#   --prefix /usr/local       install under that prefix (needs write access)
#   --base-url URL|DIRECTORY  fetch from a mirror of a release's assets
#   --archive path.tar.gz     install an archive already downloaded
#   --dry-run                 download and verify, install nothing

set -eu

RELEASES="https://github.com/ntatschner/loadout-cli/releases"

say() {
    printf '%s\n' "$*"
}

fail() {
    printf 'loadout install: %s\n' "$*" >&2
    exit 1
}

# curl or wget, whichever is here, or a plain copy when the source is a local
# directory: a mirror on a file share is a mirror too.
fetch() {
    case "$1" in
        http://*|https://*)
            if command -v curl >/dev/null 2>&1; then
                curl -fsSL --proto '=https,http' -o "$2" "$1"
            elif command -v wget >/dev/null 2>&1; then
                wget -q -O "$2" "$1"
            else
                fail "neither curl nor wget is installed, so nothing can be downloaded."
            fi
            ;;
        *)
            cp -- "$1" "$2"
            ;;
    esac
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum -- "$1" | cut -d ' ' -f 1
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 -- "$1" | cut -d ' ' -f 1
    else
        return 1
    fi
}

# The .NET runtime identifier for this machine, as the release names archives.
platform() {
    case "$(uname -s)" in
        Darwin) os=osx ;;
        Linux) os=linux ;;
        *)
            fail "$(uname -s) isn't supported by this script. On Windows, use install.ps1 from the same release."
            ;;
    esac

    case "$(uname -m)" in
        x86_64|amd64) arch=x64 ;;
        arm64|aarch64) arch=arm64 ;;
        *) fail "no build is published for $(uname -m)." ;;
    esac

    # A shell running under Rosetta reports x86_64 on an Apple Silicon Mac. The
    # Intel build would run there, translated, when the native one is sitting
    # beside it.
    if [ "$os" = osx ] && [ "$arch" = x64 ] \
        && [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || true)" = 1 ]; then
        arch=arm64
    fi

    # The Linux builds link against glibc. On musl, Alpine for one, the binary
    # would be installed and then fail to start with a message about a missing
    # file that is not missing.
    if [ "$os" = linux ] && ldd --version 2>&1 | grep -qi musl; then
        fail "this is a musl system. The Linux builds need glibc."
    fi

    printf '%s-%s' "$os" "$arch"
}

# The release's feed names its version, which is what the archive names carry.
# Read with sed rather than a JSON parser, because nothing guarantees one is
# installed; the field is one line of a file this project writes.
version_from_feed() {
    sed -n 's/^[[:space:]]*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$1" | head -n 1
}

# macOS quarantines anything downloaded through a browser. Until the binary is
# signed and notarised, Gatekeeper blocks it, and the honest fix is to remove
# the attribute from the files this installed rather than to tell people to
# disable Gatekeeper, which spec section 85 forbids. A download through this
# script is never quarantined; an --archive from a browser is.
clear_quarantine() {
    if [ "$(uname -s)" = "Darwin" ] && command -v xattr >/dev/null 2>&1; then
        if xattr -p com.apple.quarantine "$1" >/dev/null 2>&1; then
            xattr -d com.apple.quarantine "$1" 2>/dev/null || true
            say "Removed the download quarantine attribute from $(basename -- "$1")."
        fi
    fi
}

main() {
    prefix="${HOME}/.local"
    archive=""
    version=""
    base=""
    dry_run=0

    while [ $# -gt 0 ]; do
        case "$1" in
            --prefix) [ $# -ge 2 ] || fail "--prefix needs a value."; prefix="$2"; shift 2 ;;
            --archive) [ $# -ge 2 ] || fail "--archive needs a value."; archive="$2"; shift 2 ;;
            --version) [ $# -ge 2 ] || fail "--version needs a value."; version="${2#v}"; shift 2 ;;
            --base-url) [ $# -ge 2 ] || fail "--base-url needs a value."; base="${2%/}"; shift 2 ;;
            --dry-run) dry_run=1; shift ;;
            -h|--help)
                say "Installs loadout on Linux or macOS."
                say ""
                say "  --version 0.50.0          install that release rather than the latest"
                say "  --prefix /usr/local       install under that prefix (default ~/.local)"
                say "  --base-url URL|DIRECTORY  fetch from a mirror of a release's assets"
                say "  --archive path.tar.gz     install an archive already downloaded"
                say "  --dry-run                 download and verify, install nothing"
                exit 0
                ;;
            *) fail "unknown option $1. See --help." ;;
        esac
    done

    temporary=$(mktemp -d)
    # shellcheck disable=SC2064
    trap "rm -rf '${temporary}'" EXIT INT TERM

    if [ -n "$archive" ]; then
        [ -f "$archive" ] || fail "no archive at ${archive}."

        # An archive somebody downloaded by hand is checked against whatever
        # manifest came with it, if one did. One built locally has none, so
        # its absence is said rather than refused.
        manifest=""
        if [ -f "${archive}.sha256" ]; then
            manifest="${archive}.sha256"
        elif [ -f "$(dirname -- "$archive")/SHA256SUMS" ]; then
            manifest="$(dirname -- "$archive")/SHA256SUMS"
        fi

        name=$(basename -- "$archive")
        package="$archive"
    else
        rid=$(platform)

        if [ -z "$base" ]; then
            if [ -n "$version" ]; then
                base="${RELEASES}/download/v${version}"
            else
                base="${RELEASES}/latest/download"
            fi
        fi

        if [ -z "$version" ]; then
            fetch "${base}/feed.json" "${temporary}/feed.json" \
                || fail "couldn't read ${base}/feed.json to find the version."
            version=$(version_from_feed "${temporary}/feed.json")
            [ -n "$version" ] || fail "${base}/feed.json doesn't name a version."
        fi

        name="loadout-${version}-${rid}.tar.gz"
        package="${temporary}/${name}"
        manifest="${temporary}/SHA256SUMS"

        say "Downloading loadout ${version} for ${rid}..."
        fetch "${base}/${name}" "$package" || fail "couldn't download ${base}/${name}."

        # A download is never installed unchecked: the manifest must exist and
        # must name this archive.
        fetch "${base}/SHA256SUMS" "$manifest" || fail "couldn't download ${base}/SHA256SUMS."
    fi

    if [ -n "$manifest" ]; then
        # sha256sum writes "hash  name", or "hash *name" in binary mode; a
        # sidecar holds one such line. Matched on the name rather than taken
        # from the first line, so a manifest for other files is not believed.
        expected=$(awk -v n="$name" '$2 == n || $2 == "*" n { print $1; exit }' "$manifest")
        [ -n "$expected" ] || fail "${manifest##*/} doesn't list ${name}. Not installing."

        actual=$(sha256_of "$package") \
            || fail "no sha256sum or shasum is installed, so the download can't be checked. Not installing."

        [ "$actual" = "$expected" ] || fail "checksum mismatch for ${name}. Not installing."
        say "Checksum verified."
    else
        say "No checksum file beside ${name}; installing it unverified." >&2
    fi

    tar -xzf "$package" -C "$temporary"
    [ -f "${temporary}/loadout" ] || fail "the archive does not contain loadout."

    bin_dir="${prefix}/bin"

    if [ "$dry_run" = 1 ]; then
        say "Dry run: would install loadout into ${bin_dir}. Nothing was installed."
        return 0
    fi

    mkdir -p "$bin_dir"

    # Installed with an explicit mode rather than a plain copy: an archive built
    # on a Windows machine can arrive without the executable bit, and a binary
    # that will not run is a confusing way to finish an install.
    install -m 0755 "${temporary}/loadout" "${bin_dir}/loadout"
    say "Installed ${bin_dir}/loadout"
    clear_quarantine "${bin_dir}/loadout"

    # The archive carries native libraries beside the binary — libonigwrap.dylib
    # on macOS, libonigwrap.so on Linux — which the single-file bundle does not
    # contain. The runtime looks for them in the executable's own directory, so
    # they go into the same one.
    for library in "${temporary}"/lib*.dylib "${temporary}"/lib*.so; do
        [ -f "$library" ] || continue
        install -m 0755 "$library" "${bin_dir}/$(basename -- "$library")"
        say "Installed ${bin_dir}/$(basename -- "$library")"
        clear_quarantine "${bin_dir}/$(basename -- "$library")"
    done

    case ":${PATH}:" in
        *":${bin_dir}:"*)
            ;;
        *)
            say ""
            say "${bin_dir} is not on your PATH. Add it with:"
            say ""
            say "  echo 'export PATH=\"${bin_dir}:\$PATH\"' >> ~/.profile"
            say ""
            ;;
    esac

    say ""
    say "Next: loadout setup"
}

# Everything runs from here, on the last line, so a download cut off halfway
# through piping into sh runs nothing rather than half an install.
main "$@"
