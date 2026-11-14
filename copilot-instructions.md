# BEKP Repository Instructions

## Project

This repository (BEKP) hosts the hospital consent form management application ("Quản lý Phiếu đồng thuận"): patient consent/commitment forms, CCCD QR scanning, HIS (FPT eHospital) lookup, and VNPT SmartCA digital signatures.

See `docs/KE_HOACH_TRIEN_KHAI.md` (approved plan), `docs/yeu-cau-nghiep-vu.md` (requirements) and `docs/kien-truc.md` (architecture).

## Technology stack

The stack below was mandated by the project owner and supersedes earlier defaults (PostgreSQL, Material UI, .NET 9).

- Backend: ASP.NET Core .NET 10 Web API (RESTful, `/api/v1`)
- Architecture: Clean Architecture
- Database: Microsoft SQL Server
- ORM: Entity Framework Core 10 (migrations with the local `dotnet-ef` tool)
- Frontend: React, TypeScript, Vite
- UI: Ant Design (Vietnamese locale `vi_VN`)
- API client: TanStack Query
- Routing: React Router
- Infrastructure: Docker Compose (development/testing), GitHub Actions (CI)
- Testing: xUnit for backend and Vitest + Testing Library for frontend
- Frontend lint: oxlint

## Required solution structure

backend/
- ConsentManagement.slnx
- src/Consent.Api
- src/Consent.Application
- src/Consent.Domain
- src/Consent.Infrastructure
- src/Consent.Persistence
- src/Consent.Shared
- tests/ (Domain, Application, Api integration, Architecture tests)

frontend/
docs/
scripts/

## Architecture rules

- Domain must not depend on Application, Infrastructure, Persistence, or API.
- Application may depend only on Domain and Shared.
- Infrastructure and Persistence implement interfaces defined in Application.
- API contains controllers, middleware, authentication configuration, and dependency injection only.
- Do not put business logic in controllers.
- Use dependency injection.
- Use async APIs and CancellationToken.
- Use nullable reference types.
- Enable warnings as errors where practical.
- Use UTC timestamps.
- Use UUID primary keys internally.
- Business entity codes such as DEP-0001 and PDT-2026-000001 are separate fields.
- Layer dependency rules are enforced by `backend/tests/Consent.ArchitectureTests`; keep them passing.
- Return errors as ProblemDetails (RFC 9457) with Vietnamese titles; never expose exception details.
- User-facing text is Vietnamese.
- Do not use deprecated Ant Design APIs (e.g. `Alert` `title`, not `message`; `Space` `orientation`, not `direction`). Frontend tests fail on any Ant Design console warning, because Vitest hides the console output of passing tests.

## Security rules

- Never commit secrets, passwords, connection strings, access tokens, or patient data.
- Use environment variables for secrets.
- Do not include real patient data.
- Every endpoint requires authentication by default (fallback policy); mark public endpoints with `[AllowAnonymous]` explicitly.
- Protect actions with `[HasPermission(Permissions.X)]`; add new permission codes to `PermissionCatalog` and default roles in `SystemRoles`.
- Entities whose changes must be audited implement `IAuditableEntity`; mark sensitive properties with `[SensitiveData]` so the audit interceptor masks them. Never add update/delete paths for `AuditLogs`.
- Personal identifiers (CCCD, old ID, phone, health-insurance number) are encrypted at rest via the EF value converter (`ConfigureEncryption`) and looked up through the HMAC blind index; never put them in URLs or logs, and return them masked in lists.
- Validate all input.
- Use secure defaults.

## Quality rules

Before completing a task:

1. Restore dependencies.
2. Build backend.
3. Run backend tests.
4. Install frontend dependencies.
5. Run frontend lint.
6. Run frontend tests.
7. Build frontend.
8. Ensure Docker Compose configuration is valid.
9. Update README when setup or behavior changes.

`./scripts/run-checks.sh` runs steps 1–8 in the same way as CI.

## Pull requests

- Keep each pull request focused on one GitHub issue.
- Include a summary, changed files, test results, and known limitations.
- Do not merge directly to main.
- Use a feature branch.
