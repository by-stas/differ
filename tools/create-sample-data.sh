#!/usr/bin/env bash
# Creates two sample folders (samples/left and samples/right) that exercise every
# comparison case: added, removed, modified, unchanged, archives and nested archives.
set -euo pipefail

ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/samples}"
LEFT="$ROOT/left"
RIGHT="$ROOT/right"

rm -rf "$ROOT"
mkdir -p "$LEFT" "$RIGHT"

# Unchanged on both sides.
for side in "$LEFT" "$RIGHT"; do
  mkdir -p "$side/logs" "$side/src"
  printf 'app started\napp ready\n' > "$side/logs/app.log"
  printf 'export const version = 1;\n' > "$side/src/version.ts"
done

# Modified text files.
printf '{\n  "mode": "debug",\n  "retries": 3\n}\n' > "$LEFT/config.json"
printf '{\n  "mode": "release",\n  "retries": 5,\n  "timeoutMs": 30000\n}\n' > "$RIGHT/config.json"

printf '# Release notes\n\n- initial version\n' > "$LEFT/README.md"
printf '# Release notes\n\n- initial version\n- archive comparison\n' > "$RIGHT/README.md"

# Removed and added.
printf 'this file is only in the left folder\n' > "$LEFT/old-notes.txt"
printf 'this file is only in the right folder\n' > "$RIGHT/new-file.txt"
mkdir -p "$LEFT/legacy"
printf 'obsolete\n' > "$LEFT/legacy/obsolete.cfg"
mkdir -p "$RIGHT/src/feature"
printf 'export const feature = true;\n' > "$RIGHT/src/feature/index.ts"

# Same size, different content: only a hash comparison finds this one.
printf 'AAAA\n' > "$LEFT/checksum.txt"
printf 'BBBB\n' > "$RIGHT/checksum.txt"

# Binary file that cannot be diffed as text.
head -c 2048 /dev/urandom > "$LEFT/assets.bin"
head -c 3072 /dev/urandom > "$RIGHT/assets.bin"

build_archive() {
  local target="$1" settings="$2" extra_name="$3" extra_content="$4"
  local work
  work="$(mktemp -d)"

  mkdir -p "$work/config" "$work/images"
  printf '%s\n' "$settings" > "$work/config/settings.json"
  printf 'shared entry\n' > "$work/images/manifest.txt"
  printf '%s\n' "$extra_content" > "$work/$extra_name"

  # A nested archive so ArchiveMaxDepth >= 2 has something to find.
  local inner
  inner="$(mktemp -d)"
  printf '%s\n' "$settings" > "$inner/inner-config.json"
  (cd "$inner" && zip -q -r "$work/packages-inner.zip" .)
  mkdir -p "$work/packages"
  mv "$work/packages-inner.zip" "$work/packages/inner.zip"

  rm -f "$target"
  (cd "$work" && zip -q -r "$target" .)
  rm -rf "$work" "$inner"
}

if command -v zip > /dev/null 2>&1; then
  build_archive "$LEFT/package.zip" '{ "debug": true }' 'readme.txt' 'only in the left archive'
  build_archive "$RIGHT/package.zip" '{ "debug": false }' 'settings-extra.json' 'only in the right archive'
else
  echo "warning: 'zip' is not installed, skipping the sample archives" >&2
fi

echo "Left folder:  $LEFT"
echo "Right folder: $RIGHT"
