# User Management Module

Full-stack user management module: ASP.NET Core (.NET 10) Clean Architecture Web API + Angular 21 admin SPA, SQL Server 2022, JWT authentication, role-based authorization, soft delete, audit trail, and English/Arabic localization with RTL.

> **Status: Phase 1 — Analysis and Architecture (design review gate).**
> No application code has been written yet. This is deliberate: the design is verified before implementation begins.

## Phase 1 deliverables

| Document | Contents |
|---|---|
| [docs/01-requirements-checklist.md](docs/01-requirements-checklist.md) | Every requirement extracted from the assignment, with the planned implementation and delivery phase |
| [docs/02-architecture.md](docs/02-architecture.md) | Clean Architecture layering, dependency rules, cross-cutting design, request pipeline |
| [docs/03-project-structure.md](docs/03-project-structure.md) | Complete repository and project tree, backend and frontend |
| [docs/04-domain-model.md](docs/04-domain-model.md) | Entities, value objects, invariants, business rules |
| [docs/05-database-model.md](docs/05-database-model.md) | Physical schema, constraints, indexes, query plans, seed strategy |
| [docs/06-api-contract.md](docs/06-api-contract.md) | Endpoints, DTOs, status codes, ProblemDetails, paging/sorting/filtering contract |
| [docs/07-authorization-matrix.md](docs/07-authorization-matrix.md) | Capability matrix, policies, enforcement points, IDOR and mass-assignment defences |
| [docs/08-security-plan.md](docs/08-security-plan.md) | Threat model, OWASP Top 10 mapping, controls, secret management |
| [docs/09-localization-plan.md](docs/09-localization-plan.md) | Backend and frontend i18n architecture, en/ar, RTL |
| [docs/10-testing-plan.md](docs/10-testing-plan.md) | Unit, integration and frontend test inventory mapped to behaviour |
| [docs/11-implementation-plan.md](docs/11-implementation-plan.md) | Phases 2-10, verification gates, git commit plan |
| [docs/12-decision-log.md](docs/12-decision-log.md) | Architectural decision records with rationale and consequences |
| [docs/13-audit-policy.md](docs/13-audit-policy.md) | Normative audit policy: audited entities and operations, captured, redacted and never-stored fields |

## Target stack

| Concern | Choice | Verified on this machine |
|---|---|---|
| Backend runtime | .NET 10 LTS (SDK 10.0.111) | Yes |
| ORM | EF Core 10, SQL Server provider | Pending Phase 2 |
| Database | SQL Server 2022 (local `MSSQLSERVER` instance available; Docker Compose for reviewers) | Yes |
| Frontend | Angular 21 (CLI 21.0.2), standalone components, signals, Angular Material 21 | Yes |
| Node.js | **24 LTS — required**, pinned by `frontend/.nvmrc` and `engines` | Not yet installed (see below) |
| Test runners | xUnit + NSubstitute (backend), Vitest + Angular testing utilities (frontend), Testcontainers for SQL integration tests, Playwright smoke suite | Docker 29.7 running, SQL Server 2022 image pulled |

> **Prerequisite:** the installed Node.js is v25.9.0 — odd-numbered, never LTS, and outside Angular 21's
> supported range (`^20.19 || ^22.12 || ^24`). **Install Node 24 LTS before Phase 7.** Angular, Material and
> CDK all stay on the 21 major (ADR-0017). Backend phases 2-6 are unaffected.

## Next step

Review the Phase 1 documents and approve, or request changes. Implementation starts at
[Phase 2 — Backend foundation](docs/11-implementation-plan.md#phase-2--backend-foundation).
