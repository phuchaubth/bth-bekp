#!/usr/bin/env bash
# Same package as publish-iis.ps1, for CI and Linux build agents. Usage: publish-iis.sh [output] [basePath]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
output="${1:-$root/artifacts/iis}"
base="${2:-/}"
[[ "$base" == /* ]] || base="/$base"
[[ "$base" == */ ]] || base="$base/"

(cd "$root/frontend" && npm ci && VITE_BASE_PATH="$base" npm run build)
dotnet publish "$root/backend/BEKP.Api/BEKP.Api.csproj" -c Release -o "$output"
mkdir -p "$output/wwwroot" "$output/logs"
cp -R "$root/frontend/dist/." "$output/wwwroot/"
echo "IIS package ready: $output"
