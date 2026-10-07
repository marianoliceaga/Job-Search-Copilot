#!/usr/bin/env bash
# Fails if signing material for the Android app could be committed (tracked, or untracked and not ignored).
# The owner's keystore lives outside the repo; CI reads it from GitHub secrets. Runs in .github/workflows/android.yml
# next to the repo-wide scripts/check-no-personal-data.sh.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

found=$(git ls-files --cached --others --exclude-standard \
    | grep -Ei '\.(keystore|jks|p12|pfx|apk|aab)$|(^|/)signing[^/]*\.txt$' || true)

if [ -n "$found" ]; then
    echo "✗ Signing material or built packages that must not be committed:"
    echo "$found" | sed 's/^/    /'
    exit 1
fi

echo "✓ No Android signing material or packages"
