#!/usr/bin/env bash
# Runs every quality gate locally: backend build + tests, frontend lint + tests + build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

echo "== Backend"
(cd "$root/backend" && dotnet restore BEKP.slnx && dotnet build BEKP.slnx --no-restore && dotnet test BEKP.slnx --no-build)

echo "== Frontend (npm >= 11)"
(cd "$root/frontend" && npm ci && npm run lint && npm test && npm run build)

echo "All checks passed."
