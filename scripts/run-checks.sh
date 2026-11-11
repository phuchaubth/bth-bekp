#!/usr/bin/env bash
# Chạy toàn bộ kiểm tra giống CI: build + test backend, lint + typecheck + test + build frontend,
# và kiểm tra cấu hình Docker Compose.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "==> Backend: restore, build, test"
(cd "$ROOT/backend" && dotnet restore && dotnet build --no-restore -c Release && dotnet test --no-build -c Release)

echo "==> Frontend: install, lint, typecheck, test, build"
(cd "$ROOT/frontend" && npm ci && npm run lint && npm run typecheck && npm test && npm run build)

echo "==> Docker Compose: validate"
(cd "$ROOT" && MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-placeholder-for-validation}" docker compose config --quiet)

echo "Tất cả kiểm tra đều đạt."
