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

# Google service account keys (the Play upload credential lives only in the PLAY_SERVICE_ACCOUNT_JSON secret).
keys=$(git ls-files --cached --others --exclude-standard -z \
    | xargs -0 -r grep -lE '"type"[[:space:]]*:[[:space:]]*"service_account"|-----BEGIN (RSA |ENCRYPTED )?PRIVATE KEY-----' 2>/dev/null \
    | grep -v '^android/scripts/check-no-signing-keys.sh$' || true)

if [ -n "$keys" ]; then
    echo "✗ Private keys or Google service account keys that must not be committed:"
    echo "$keys" | sed 's/^/    /'
    exit 1
fi

echo "✓ No Android signing material, packages or service account keys"
