#!/usr/bin/env bash
# Fails if files that could be committed (tracked, or untracked and not ignored) contain
# personal data or secrets. Runs in CI and in the optional pre-commit hook (.githooks/).
#
# Checks:
#   1. Forbidden files: SQLite databases, secrets, .env, OAuth client secrets.
#   2. CV-like documents (.pdf/.doc/.docx) outside tests/**/Fixtures/ (fixtures must be fictional).
#   3. Google API keys.
#   4. E-mail addresses outside the example/no-reply allowlist.
#   5. Literals listed in the local, git-ignored .pii-denylist file (one per line), if present.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

failed=0
report() {
    echo "✗ $1"
    echo "$2" | sed 's/^/    /'
    failed=1
}

files=$(git ls-files --cached --others --exclude-standard | while read -r f; do [ -f "$f" ] && echo "$f"; done)

forbidden=$(echo "$files" | grep -Ei '\.(db|db-shm|db-wal|sqlite|sqlite3)$|(^|/)secrets\.json$|(^|/)\.env(\..*)?$|(^|/)client_secret[^/]*\.json$' || true)
[ -n "$forbidden" ] && report "Forbidden files (databases, secrets):" "$forbidden"

documents=$(echo "$files" | grep -Ei '\.(pdf|doc|docx)$' | grep -Ev '^tests/.*/Fixtures/' || true)
[ -n "$documents" ] && report "Documents outside tests/**/Fixtures/ (possible real CV):" "$documents"

grep_files() {
    # $1: extended regex. Prints file:line:match for every text file that matches.
    echo "$files" | grep -v '^scripts/check-no-personal-data.sh$' | tr '\n' '\0' \
        | xargs -0 -r grep -nIoE -- "$1" 2>/dev/null || true
}

keys=$(grep_files 'AIza[0-9A-Za-z_-]{35}' | sed -E 's/AIza[0-9A-Za-z_-]{35}/AIza*** (masked)/')
[ -n "$keys" ] && report "Google API keys:" "$keys"

emails=$(grep_files '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' \
    | grep -viE '@(example\.(com|org|net)|users\.noreply\.github\.com)$' || true)
[ -n "$emails" ] && report "E-mail addresses (only @example.com/.org/.net are allowed):" "$emails"

if [ -f .pii-denylist ]; then
    while IFS= read -r literal || [ -n "$literal" ]; do
        [ -z "$literal" ] && continue
        [ "${literal:0:1}" = "#" ] && continue
        hits=$(echo "$files" | grep -v '^\.pii-denylist$' | tr '\n' '\0' | xargs -0 -r grep -nIlF -- "$literal" 2>/dev/null || true)
        [ -n "$hits" ] && report "Denylisted personal data found (literal hidden):" "$hits"
    done < .pii-denylist
fi

if [ "$failed" -ne 0 ]; then
    echo "Personal data check failed."
    exit 1
fi
echo "✓ Personal data check passed."
