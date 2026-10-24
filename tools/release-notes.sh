#!/usr/bin/env bash
# Prints the release notes for a tag (for example v0.6.0): that version's CHANGELOG.md section, a downloads table, and the usual footer.
set -euo pipefail
tag="${1:?usage: release-notes.sh vX.Y.Z}"
ver="${tag#v}"
repo="https://github.com/starmast/zero-company-save-editor"
cd "$(dirname "$0")/.."

# Everything between "## [ver]" and the next "## [" heading, with its "###" headings kept as they are.
section=$(awk -v h="## [$ver]" 'index($0, h) == 1 { on = 1; next } on && /^## \[/ { exit } on { print }' CHANGELOG.md)
[ -n "$(echo "$section" | tr -d '[:space:]')" ] || { echo "CHANGELOG.md has no section for $ver" >&2; exit 1; }

printf '%s\n\n' "$section" | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}'
cat <<NOTES

### Downloads
| System | File |
|---|---|
| Windows 10/11 | \`ZeroCompanyEditor-$tag-win-x64.zip\` |
| Linux | \`ZeroCompanyEditor-$tag-linux-x64.tar.gz\` |
| macOS (Apple Silicon) | \`ZeroCompanyEditor-$tag-osx-arm64.tar.gz\` |
| macOS (Intel) | \`ZeroCompanyEditor-$tag-osx-x64.tar.gz\` |

Unpack and run \`ZeroCompanyEditor\`; nothing else needs installing. The \`zc\` command line tool is in the same folder (\`zc --help\`). macOS builds are unsigned (right-click > Open). Linux and macOS are less tested than Windows. See the [README]($repo#readme) and [docs]($repo/tree/main/docs).
NOTES
