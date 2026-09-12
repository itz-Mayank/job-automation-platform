# JobForge

A small, production-minded job automation platform. Users create HTTP jobs, run them manually or on a
schedule, and get a clear, honest picture of what happened — including failures, retries, and recovery
from a crashed worker.

## Overview

JobForge lets a developer:

- Define an HTTP request (method, URL, headers, JSON body, timeout, retry policy) as a **job**.
- Run it on demand (**Run Now**) or on a fixed interval.
- See every **execution**'s full history: attempt count, worker that ran it, timing, HTTP status,
  truncated response body, and — for failures — exactly what went wrong and whether/when it will retry.
- Retry a failed execution manually without losing the original's history.

The system is built to survive the failure modes a real job runner has to survive: duplicate "Run Now"
clicks, two workers racing for the same job, a worker crashing mid-execution, and the target API timing
out or returning errors — see [ENGINEERING.md](ENGINEERING.md) for how each is handled.

## Features

- Email/password auth (JWT), scoped so users only ever see their own jobs and executions.
- Job CRUD, pause/resume, manual "Run Now", interval scheduling (1m/5m/15m/1h/1d).
- Execution history with status filtering, retry, and a detail page built to answer *what failed, why,
  which attempt, which worker, and will/when it retries*.
- A dashboard with job/execution counts and a recent-activity feed.
- Multiple worker processes safely sharing one PENDING queue (Postgres `FOR UPDATE SKIP LOCKED`).
- At-most-one-active-execution-per-job guarantee, enforced by a database constraint, not just an
  application check — verified under real concurrent load in the test suite.
- Exponential backoff retries with a bounded attempt count; stale (crashed-worker) execution recovery.
- Basic SSRF hardening (job URLs and the executor's outbound connections both reject
  loopback/private/link-local addresses).

## Architecture

```
Browser → Next.js (React/TS) → ASP.NET Core API → PostgreSQL
                                                       ↑
                                        ┌──────────────┼──────────────┐
                                        │              │              │
                                    Worker 1       Worker 2       Worker N
                                        │              │              │
                                        └──────── HTTP Executor ──────┘
                                                       │
                                              External target APIs
```

PostgreSQL is both the system of record **and** the coordination layer for execution claiming — there
is no separate queue (Redis/RabbitMQ/etc). One or more independent `.NET Worker Service` processes poll
for eligible executions, claim at most one at a time each via a short transaction using
`SELECT ... FOR UPDATE SKIP LOCKED`, then run the HTTP call *outside* that transaction. See
[ENGINEERING.md](ENGINEERING.md) for the full reasoning.

## Tech Stack

| Layer | Choice |
|---|---|
| Frontend | Next.js (App Router), React, TypeScript, Tailwind CSS, TanStack Query, Axios |
| Backend API | ASP.NET Core 9, C#, EF Core (Npgsql), JWT auth, Swashbuckle |
| Worker | .NET 9 `BackgroundService` |
| Database | PostgreSQL 16 |
| Testing | xUnit, FluentAssertions, Testcontainers (real Postgres in tests) |
| Infra | Docker, Docker Compose |

## Project Structure

```
backend/
  JobForge.sln
  src/
    JobForge.Domain/          entities, enums, exceptions, the execution state machine
    JobForge.Application/     DTOs, service interfaces, business logic (Auth/Job/Execution/Dashboard)
    JobForge.Infrastructure/  EF Core DbContext + migrations, claim service, HTTP executor, JWT/BCrypt
    JobForge.Api/             controllers, middleware, Program.cs
  tests/
    JobForge.Tests/           unit tests + Testcontainers-backed integration tests
worker/
  JobForge.Worker/            the polling BackgroundService
frontend/
  src/app/                    Next.js pages (App Router)
  src/components/             shared UI (forms, badges, nav)
  src/lib/                    API client, auth context, types
docker-compose.yml
.env.example
```

## Local Setup

Prerequisites: .NET 9 SDK, Node 22+, Docker (for Postgres, or the whole stack).

```bash
# 1. Start Postgres (or run the whole thing with Docker — see below)
docker run -d --name jobforge-postgres -e POSTGRES_DB=jobforge -e POSTGRES_USER=jobforge \
  -e POSTGRES_PASSWORD=jobforge -p 5432:5432 postgres:16-alpine

# 2. Apply migrations
cd backend
dotnet tool install --global dotnet-ef   # if you don't already have it
dotnet ef database update --project src/JobForge.Infrastructure --startup-project src/JobForge.Api

# 3. Run the API
dotnet run --project src/JobForge.Api

# 4. Run the worker (separate terminal)
cd ../worker/JobForge.Worker && dotnet run

# 5. Run the frontend (separate terminal)
cd ../../frontend
cp .env.local.example .env.local   # or just set NEXT_PUBLIC_API_URL
npm install
npm run dev
```

Open http://localhost:3000, register an account, and create a job.

## Environment Variables

See [.env.example](.env.example) for the full list used by Docker Compose. Locally without Docker, the
equivalents live in `backend/src/JobForge.Api/appsettings.json` and
`worker/JobForge.Worker/appsettings.json`:

| Variable | Used by | Purpose |
|---|---|---|
| `ConnectionStrings__Postgres` | API, Worker | Postgres connection string |
| `Jwt__Secret` / `Jwt__Issuer` / `Jwt__Audience` | API | JWT signing/validation |
| `Cors__AllowedOrigins__0` | API | Origin allowed to call the API from a browser |
| `ApplyMigrationsOnStartup` | API | Explicit opt-in to run EF migrations at boot (see ENGINEERING.md) |
| `Worker__WorkerId` | Worker | Identifies this process in logs/`worker_id`; auto-generated if unset |
| `Worker__PollIntervalSeconds` | Worker | How often to poll when there's nothing to claim |
| `Worker__StaleExecutionThresholdSeconds` | Worker | How long a RUNNING execution can go silent before it's recovered |
| `NEXT_PUBLIC_API_URL` | Frontend (build-time) | API URL the browser calls |

## Running With Docker

```bash
cp .env.example .env   # edit JWT_SECRET and passwords for anything beyond local testing
docker compose up --build
```

This starts Postgres, the API (applies migrations on boot per `APPLY_MIGRATIONS_ON_STARTUP=true`), one
worker, and the frontend. Open http://localhost:3000.

To see multiple workers safely sharing the queue:

```bash
docker compose up --build --scale worker=3
```

## Running Tests

```bash
cd backend
dotnet test tests/JobForge.Tests
```

Tests spin up a real, disposable Postgres container via Testcontainers (Docker must be running) — this
is deliberate: the properties under test (row locking, the partial unique index) are Postgres-specific
and an in-memory fake would prove nothing. Includes: auth, ownership enforcement (User A vs. User B),
job validation, the concurrent-Run-Now race, the two-worker claim race, retry exhaustion, stale-execution
recovery, and manual retry semantics. 58 tests, all passing as of this writing.

## Database Migrations

```bash
cd backend
# create a new migration after changing an entity/configuration
dotnet ef migrations add <Name> --project src/JobForge.Infrastructure --startup-project src/JobForge.Api

# apply migrations
dotnet ef database update --project src/JobForge.Infrastructure --startup-project src/JobForge.Api

# reset local DB (drops and recreates)
dotnet ef database drop --project src/JobForge.Infrastructure --startup-project src/JobForge.Api -f
dotnet ef database update --project src/JobForge.Infrastructure --startup-project src/JobForge.Api
```

The API does **not** run migrations implicitly on every boot by default — see ENGINEERING.md.

## API Documentation

Swagger UI is served at `/swagger` on the API (e.g. http://localhost:8080/swagger or
http://localhost:5080/swagger locally). Include a JWT via the "Authorize" button
(`Bearer <token>`, obtained from `/api/auth/login`).

## Worker Architecture

The worker is a plain `BackgroundService` running a poll loop. Each tick:

1. Recovers RUNNING executions whose worker went silent past the stale threshold.
2. Promotes RETRYING executions whose backoff window has elapsed back to PENDING.
3. Creates PENDING executions for interval-scheduled jobs that are due.
4. Attempts to claim and fully process one PENDING execution (claim → execute → persist result).

Any number of worker processes can run this loop concurrently against the same database safely — see
ENGINEERING.md "Concurrency".

## Deployment

**Live**: https://job-automation-platform-nine.vercel.app (frontend) · https://jobforge-api.onrender.com
(API, Swagger at `/swagger`) — Postgres + API + worker on Render, frontend on Vercel, verified
end-to-end with a real browser session. See ENGINEERING.md §11 for the exact setup and two
deployment-specific caveats worth knowing before you rely on it: both Render services (including the
worker) sleep after ~15 minutes idle, and the free Postgres instance expires after 30 days.

## Demo Credentials

No seeded demo account exists — registration is open and takes seconds
(`POST /api/auth/register` or the `/register` page). This avoids committing any password, even a
placeholder one, to the repo.
