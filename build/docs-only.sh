#!/usr/bin/env bash
# Prints "true" when every file changed since BASE is documentation, and
# "false" otherwise. CI uses it to test a docs-only change on one platform
# instead of six.
#
# Documentation means a Markdown file at the top of the repository, or anything
# under docs/. Not every .md file: the specialists under src/ are Markdown and
# ship inside the launcher, so a change to one is a change to the product.
#
# Anything it cannot answer - no base, a base the clone does not have, nothing
# changed - is "false", so the doubtful case gets the full build.
#
# Usage: build/docs-only.sh BASE [HEAD]
set -uo pipefail

base="${1:-}"
head="${2:-HEAD}"

if [ -z "$base" ] || [ "$base" = "0000000000000000000000000000000000000000" ] \
  || ! git cat-file -e "$base^{commit}" 2>/dev/null; then
  echo false
  exit 0
fi

changed="$(git diff --name-only "$base" "$head")"

if [ -z "$changed" ]; then
  echo false
  exit 0
fi

if printf '%s\n' "$changed" | grep -qvE '^(docs/.+|[^/]+\.md)$'; then
  echo false
else
  echo true
fi
