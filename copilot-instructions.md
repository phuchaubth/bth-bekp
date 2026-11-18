# BEKP Repository Instructions

## Project

This repository contains the BTH Enterprise Knowledge Platform (BEKP), a hospital quality, JCI, risk, CAPA, evidence, survey, and AI governance platform.

## Technology stack

- Backend: ASP.NET Core 10 Web API (.NET 10)
- Architecture: Clean Architecture
- Database: Microsoft SQL Server (separate "BEKP" database on the shared hospital SQL Server)
- ORM: Entity Framework Core
- Frontend: React, TypeScript, Vite
- UI: Material UI
- API client: TanStack Query
- Hosting: IIS on Windows Server (ASP.NET Core Module, in-process); the API serves the React build from wwwroot. No Docker.
- Testing: xUnit for backend and Vitest for frontend

## Required solution structure

backend/
- BEKP.Api
- BEKP.Application
- BEKP.Domain
- BEKP.Infrastructure
- BEKP.Persistence
- BEKP.Shared
- tests/

frontend/
database/
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
- Business entity codes such as DEP-0001 and AI-000001 are separate fields.

## Security rules

- Never commit secrets, passwords, connection strings, access tokens, or patient data.
- Use environment variables for secrets.
- Do not include real patient data.
- Prepare the application for RBAC and audit logging.
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
8. Ensure the IIS publish package builds (scripts/publish-iis.ps1 or publish-iis.sh).
9. Update README when setup or behavior changes.

## Pull requests

- Keep each pull request focused on one GitHub issue.
- Include a summary, changed files, test results, and known limitations.
- Do not merge directly to main.
- Use a feature branch.
