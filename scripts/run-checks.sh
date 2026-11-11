#!/usr/bin/env bash
# Chạy toàn bộ kiểm tra giống CI: build + test backend, lint + typecheck + test + build frontend,
# và kiểm tra cấu hình Docker Compose.
# Yêu cầu Docker: integration test backend khởi động SQL Server bằng Testcontainers.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "==> Backend: restore, build, test"
(cd "$ROOT/backend" && dotnet restore && dotnet build --no-restore -c Release && dotnet test --no-build -c Release)

echo "==> Frontend: install, lint, typecheck, test, build"
(cd "$ROOT/frontend" && npm ci && npm run lint && npm run typecheck && npm test && npm run build)

echo "==> Docker Compose: validate"
(cd "$ROOT" && MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-placeholder-for-validation}" \
  JWT_SIGNING_KEY="${JWT_SIGNING_KEY:-placeholder-for-validation-0123456789abcdef}" \
  FIELD_ENCRYPTION_KEY="${FIELD_ENCRYPTION_KEY:-placeholder}" BLIND_INDEX_KEY="${BLIND_INDEX_KEY:-placeholder}" \
  docker compose config --quiet)

echo "Tất cả kiểm tra đều đạt."
