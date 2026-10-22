#!/usr/bin/env bash
# Finds the CI run whose build a release ships, and refuses when there is none
# it may ship.
#
# A release compiles nothing. CI on main publishes every runtime once through
# build/publish.ps1 and tests those bytes; Release promotes them. So the one
# question here is which run, and whether it is fit to promote:
#
#   For a tag: the push run on main for the tagged commit. The documented
#   release command pushes main and the tag together, which starts CI and
#   Release at the same moment, so this waits for the run to appear and then
#   to finish. It refuses a tag that is not on main (main is the only branch
#   whose runs publish anything to promote, so it would wait for a run that
#   never comes), a tag that disagrees with Directory.Build.props (the version
#   CI built and checked the binary against), a run that did not succeed, and
#   a run whose builds are missing or have expired.
#
#   By hand: the run named, or else the newest successful push run on main
#   that left all six builds. A change to documentation alone builds no
#   binaries, so its run is green and has nothing to promote; that is why
#   "newest green" alone is not enough.
#
# Writes run, sha and version to $GITHUB_OUTPUT when it is set, and prints
# them either way.
#
# Usage: find-tested-build.sh            (reads GITHUB_REF_TYPE, GITHUB_REF_NAME,
#                                         GITHUB_SHA, GITHUB_REPOSITORY)
#        REQUESTED=<run id> find-tested-build.sh
set -euo pipefail

repository=${GITHUB_REPOSITORY:?}
runtimes="win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64"

refuse() {
    echo "::error::$1" >&2
    exit 1
}

# 0 when the run still holds an unexpired loadout-<rid> for every runtime.
has_builds() {
    local names
    names=$(gh api "repos/$repository/actions/runs/$1/artifacts?per_page=100" \
        --jq '.artifacts[] | select(.expired | not) | .name')

    for runtime in $runtimes; do
        grep -qx "loadout-$runtime" <<<"$names" || return 1
    done
}

version_at() {
    git show "$1:Directory.Build.props" | sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' | head -1
}

# What a run is, in one call: its conclusion, branch, event and commit.
describe() {
    gh run view "$1" --repo "$repository" --json conclusion,headBranch,event,headSha \
        --jq '"\(.conclusion) \(.headBranch) \(.event) \(.headSha)"'
}

git fetch --quiet origin main

if [ "${GITHUB_REF_TYPE:-}" = tag ]; then
    tag=${GITHUB_REF_NAME:?}
    sha=${GITHUB_SHA:?}

    git merge-base --is-ancestor "$sha" origin/main ||
        refuse "$tag is on a commit that is not on main. Only main's CI runs publish a build a release can ship, so there is nothing to wait for. Tag a commit on main."

    version=$(version_at "$sha")
    [ "v$version" = "$tag" ] ||
        refuse "$tag disagrees with Directory.Build.props at that commit, which says $version. CI built and checked the binary as $version, so shipping it as $tag would mislabel it."

    # Created by the same push, but not necessarily before this job looks.
    run=""
    for _ in $(seq 1 40); do
        run=$(gh run list --repo "$repository" --workflow ci.yml --commit "$sha" \
            --event push --branch main --json databaseId --jq '.[0].databaseId // empty')
        [ -n "$run" ] && break
        sleep 15
    done
    [ -n "$run" ] || refuse "No CI run on main for $sha appeared within ten minutes."

    echo "Waiting for CI run $run to finish..."
    gh run watch "$run" --repo "$repository" --interval 30 >/dev/null || true

    read -r conclusion _ _ _ < <(describe "$run")
    [ "$conclusion" = success ] ||
        refuse "CI run $run for $sha ended '$conclusion'. Its build was not shown to work, so it is not released. Fix it on main and tag the fix."
else
    if [ -n "${REQUESTED:-}" ]; then
        run=$REQUESTED
        read -r conclusion branch event _ < <(describe "$run")
        [ "$conclusion $branch $event" = "success main push" ] ||
            refuse "CI run $run is '$conclusion' on $branch from a $event. Only a successful push run on main can be promoted."
    else
        run=""
        for candidate in $(gh run list --repo "$repository" --workflow ci.yml --branch main \
                --event push --status success --limit 50 --json databaseId --jq '.[].databaseId'); do
            if has_builds "$candidate"; then run=$candidate; break; fi
        done
        [ -n "$run" ] || refuse "None of the last 50 successful CI runs on main still holds all six builds."
    fi

    read -r _ _ _ sha < <(describe "$run")
    version=$(version_at "$sha")
fi

has_builds "$run" ||
    refuse "CI run $run no longer holds all six builds; they expire after 90 days. Release a newer commit instead."

echo "run=$run"
echo "sha=$sha"
echo "version=$version"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
    {
        echo "run=$run"
        echo "sha=$sha"
        echo "version=$version"
    } >> "$GITHUB_OUTPUT"
fi
