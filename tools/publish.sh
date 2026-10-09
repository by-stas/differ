#!/usr/bin/env bash
# Builds the Angular frontend into the API's wwwroot and publishes a single self-hosted
# application to dist/publish.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API="$ROOT/backend/FolderCompare.Api"
OUTPUT="${1:-$ROOT/dist/publish}"

echo "==> Building the Angular frontend"
(cd "$ROOT/frontend" && npm ci && npx ng build --configuration production)

echo "==> Copying the frontend into $API/wwwroot"
rm -rf "$API/wwwroot"
mkdir -p "$API/wwwroot"
cp -r "$ROOT/frontend/dist/frontend/browser/." "$API/wwwroot/"

echo "==> Publishing the API to $OUTPUT"
dotnet publish "$API/FolderCompare.Api.csproj" -c Release -o "$OUTPUT"

echo
echo "Done. Run it with:"
echo "  dotnet $OUTPUT/FolderCompare.Api.dll"
