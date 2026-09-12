# Engineering Notes

## 1. Architecture Overview

```
Next.js (browser-rendered dashboard)
   │  REST + JWT
   ▼
ASP.NET Core API  ──────────────┐
   │  EF Core                   │  EF Core
   ▼                            ▼
PostgreSQL  ◄──────────────  .NET Worker(s)
                                 │
                                 ▼
                          HttpClient → external APIs
```

The API and the worker(s) are separate processes that never talk to each other directly — they only
share the Postgres database. That's deliberate: it means the worker fleet can be scaled, restarted, or
temporarily unavailable without the API ever noticing beyond "no execution has picked this up yet,"
and vice versa. Postgres is the single source of truth for both data *and* coordination.

## 2. Job Execution Model

- **Creation.** An execution row is created in `PENDING` by one of three paths: `POST /jobs/{id}/run`
  (manual), the worker's scheduled-fan-out sweep (interval jobs whose `next_run_at` has elapsed), or
  `POST /executions/{id}/retry` (a fresh row, not a mutation of the failed one).
- **Discovery.** Workers poll; there is no push/queue. Each tick, a worker asks Postgres for "the oldest
  eligible PENDING execution" via `IExecutionClaimService`.
- **Claiming.** The claim is a single short transaction: `SELECT ... FOR UPDATE SKIP LOCKED LIMIT 1`,
  then flip that row to `RUNNING` with this worker's id and `started_at`, then commit. Nothing else
  happens inside that transaction.
- **Executing.** Only *after* the claim transaction has committed does the worker call the target URL
  (via `IJobExecutor`). The HTTP call can take up to the job's configured timeout; the database is not
  waiting on it.
- **Persisting.** The outcome (success, retryable failure, or terminal failure) is written back to the
  same row in one more transaction, via the domain's own transition methods (see §4).

## 3. Concurrency

**Duplicate executions.** `SELECT ... FOR UPDATE SKIP LOCKED` means: if worker B tries to select a row
worker A already has locked, B doesn't block and doesn't get that row — it just skips to the next
eligible one (or finds nothing). Two workers can never both come away thinking they claimed the same
execution. This is proven under real Postgres in
`ExecutionClaimConcurrencyTests.TwoConcurrentClaims_OnlyOneSucceeds` (two independent
`ExecutionClaimService` instances, each on its own connection, racing for one row).

**Concurrent Run Now.** The applicaton layer checks for an existing active execution
(PENDING/RUNNING/RETRYING) before inserting a new one — but that check-then-insert is *not* by itself
race-safe against two simultaneous requests. The actual guarantee is a Postgres partial unique index:

```sql
CREATE UNIQUE INDEX ix_job_executions_one_active_per_job
    ON job_executions (job_id)
    WHERE status IN ('Pending', 'Running', 'Retrying');
```

At most one row per job can ever have an active status, full stop, regardless of timing. `RunNowAsync`
does the cheap check first (avoids throwing in the common case), inserts, and if the insert still hits
the constraint (another request won the race in between), it catches that `DbUpdateException` and
returns the winner's execution instead of a 500. Proven under real concurrency in
`RunNowConcurrencyTests.ConcurrentRunNowRequests_CreateExactlyOneActiveExecution` (20 simultaneous HTTP
requests → exactly one execution).

## 4. State Machine

```
PENDING ──► RUNNING ──► SUCCEEDED   (terminal)
   │           │
   │           ├──► FAILED          (terminal: non-retryable error, or attempts exhausted)
   │           │
   │           └──► RETRYING ──► PENDING   (attempt++, loops back into RUNNING)
   │
   └──► CANCELLED (terminal)
RUNNING ──► CANCELLED (terminal)
RETRYING ──► CANCELLED (terminal)
```

All transitions are enforced in one place — `JobExecution`'s own methods (`MarkRunning`,
`MarkSucceeded`, `MarkRetryScheduled`, `MarkFailed`, `PromoteRetryToPending`, `Cancel`) — via a
transition table checked before any status change. `SUCCEEDED → RUNNING`, `CANCELLED → RUNNING`, etc.
throw `InvalidStateTransitionException` rather than silently succeeding. No controller or service sets
`.Status` directly.

One deliberate simplification versus a literal reading of "PENDING → RUNNING → FAILED → RETRYING →
PENDING": a *transient* failure with attempts remaining goes straight from `RUNNING` to `RETRYING`, never
through a stored `FAILED`. Writing `FAILED` for something that's about to retry would make `FAILED`
ambiguous between "terminal" and "about to retry," which defeats the point of having explicit states.
`FAILED` in this schema always means terminal.

## 5. Retry Strategy

- Exponential backoff: `delay = baseDelaySeconds × 2^(failedAttempt − 1)`, plus up to 1s of jitter to
  avoid many jobs retrying in the same instant. Base delay and max retries are per-job (`retry_delay_seconds`,
  `max_retries`); `MaxAttempts = max_retries + 1` (the first try plus that many retries).
- Retryable: network/DNS failure, timeout, HTTP 429, and HTTP 5xx.
- Not retried: HTTP 4xx other than 429 (400/401/403/404/...) — these mean the request itself is wrong
  (bad auth, bad URL, not found), and retrying an unchanged request won't fix that.
- Bounded: once `Attempt == MaxAttempts`, any further failure goes to terminal `FAILED`, never another
  `RETRYING`. There is no path that re-arms the counter automatically.
- A manual retry (`POST /executions/{id}/retry`) only accepts a `FAILED` source execution and creates a
  **new** row (`Attempt` restarts at 1, fresh `MaxAttempts` budget), linked via `retry_of_execution_id`.
  The original row's history is never touched. This was chosen over mutating the old row in place
  specifically so execution history stays a true audit log — you can always see exactly what the
  original failure looked like, even after retrying it five times.

## 6. Failure Recovery

- **Worker crash.** A worker can claim an execution (flip it to `RUNNING`) and then die — a container
  OOM-kill, a deploy, whatever — before it ever reports a result. Nothing else marks that row again on
  its own. The *next* worker's poll loop runs `RecoverStaleExecutionsAsync`, which finds `RUNNING` rows
  whose `started_at` is older than `Worker__StaleExecutionThresholdSeconds` and treats them exactly like
  a failed attempt: retry if attempts remain, terminal `FAILED` if not. This uses the same optimistic
  concurrency token (`xmin`) as the claim path, so two workers' recovery sweeps racing for the same
  stale row also can't double-process it — one gets `DbUpdateConcurrencyException` and skips it.
- **External API timeout/failure.** Caught inside `HttpJobExecutor` and converted to a structured
  `JobExecutionOutcome` — the worker loop never sees an unhandled exception from a bad target API, so one
  failing job can't take down execution of every other job.
- **Database outage.** The worker's top-level loop wraps each iteration in try/catch; an exception
  talking to Postgres is logged and the loop backs off (`Worker__ErrorBackoffSeconds`) and retries rather
  than crashing the process. The API returns a generic 500 via `ExceptionHandlingMiddleware` rather than
  leaking connection details.
- **Honest limitation — no exactly-once side effects.** If a worker successfully calls the target API and
  then crashes *before* writing the result, the execution is later recovered as if it failed and may be
  retried — even though the external side effect already happened once. Postgres locking guarantees
  exactly one worker is ever responsible for an execution at a time; it cannot guarantee the *external*
  HTTP call itself was exactly-once, because that call and the crash are outside the database's
  visibility. This is an "at-least-once" system for the side effect, by design — a truly exactly-once
  guarantee would require the target API to be idempotent (e.g. accept the same `Idempotency-Key` we
  already generate) and is out of scope here. This is stated plainly rather than glossed over.

## 7. Idempotency

`POST /jobs/{id}/run` accepts an optional `Idempotency-Key` header. If a client retries the *same*
network request (not a second user click) with the same key, the existing execution created under that
key is returned rather than a new one — checked before the active-execution check. Without a key, the
minimum guarantee still holds: the partial unique index (§3) means repeated clicks return/expose the one
active execution rather than ever creating a second one.

## 8. Database Decisions

- **Schema**: `users`, `jobs`, `job_executions`, matching the assignment's recommended shape closely
  (snake_case via `EFCore.NamingConventions`), with UUID primary keys.
- **Indexes**: `jobs(user_id)`, `jobs(next_run_at)` for the scheduler sweep, `job_executions(job_id, scheduled_at)`
  for the history page, `job_executions(status)` and `(status, scheduled_at)` for the claim/maintenance
  queries, and the partial unique index from §3.
- **Constraints**: FKs with `ON DELETE CASCADE` (deleting a job deletes its execution history — there's
  no use case for orphaned executions), `xmin`-based optimistic concurrency on `jobs` and
  `job_executions` (protects concurrent edits/claims from silently clobbering each other).
- **Why Postgres as the coordination layer** instead of Redis/RabbitMQ/SQS: `FOR UPDATE SKIP LOCKED` plus
  a partial unique index gives every guarantee this assignment asks for (safe multi-worker claiming, no
  duplicate active executions) using infrastructure already required for the data itself. Adding a
  second stateful system would roughly double the ops surface (two things to run, monitor, and keep
  consistent with the DB) for no capability this scale actually needs — see §11 for when that trade-off
  would flip.

## 9. Security

- **Auth**: JWT (HMAC-SHA256), issued on register/login, validated by ASP.NET Core's JWT bearer
  middleware on every request. Passwords hashed with BCrypt (work factor 12), never returned in any
  response.
- **Authorization**: every job/execution query is scoped to the authenticated user's id *in the query
  itself* (`WHERE job.user_id = currentUserId`), not fetched-then-checked — so a nonexistent id and
  another user's id are indistinguishable (both 404). Verified in `JobOwnershipTests`.
- **Secret handling**: request headers a job sends (including any `Authorization` header the user
  configures for the *target* API) are never logged and are only ever stored as part of the job
  definition itself, not echoed into execution error messages.
- **Response size**: response bodies are truncated to 8,000 characters by `HttpJobExecutor` before being
  returned, with a hard 20,000-character column limit as a backstop, so one enormous response can't bloat
  the database.
- **SSRF**: jobs execute arbitrary user-supplied URLs, which is a real risk for a publicly deployed
  service. Handled in two layers: (1) at job create/update time, literal loopback/private-IP hostnames
  and `localhost` are rejected outright; (2) at execution time, `HttpJobExecutor` uses a custom
  `SocketsHttpHandler.ConnectCallback` that resolves DNS itself and refuses to connect if *any* resolved
  address is loopback/private/link-local (169.254.0.0/16, which also covers the common cloud metadata
  endpoint) — this runs on every connection attempt including ones made for a redirect, so a URL that
  passes validation but later 302s to an internal address is still blocked. This is **not** a complete
  SSRF-prevention system: the DNS-resolve-then-connect approach has a small, accepted TOCTOU window if a
  hostname's DNS record changes between the check and the connect. Good enough for a take-home; a
  production system serving this exact feature would want a vetted egress proxy for defense-in-depth.

## 10. Product Decisions

- **HTTP-only jobs.** One job type keeps the executor, the schema, and the UI simple, and covers the
  overwhelming majority of real "automate a job" use cases (webhooks, health pings, sync triggers).
- **Interval scheduling, not cron.** Five fixed presets (1m/5m/15m/1h/1d) cover the common cases without
  a parser, an expression validator, or timezone edge cases a full cron implementation invites.
- **One active execution per job.** Simplest model that still answers "is this job currently doing
  something" unambiguously; a queue-of-pending-runs-per-job model is real complexity this assignment's
  scope doesn't call for.
- **Response bodies truncated, not stored in full.** Execution history is for humans debugging a job, not
  a data pipeline; nobody needs the full 50MB response, they need the first few KB and the status code.
- **Manual retry creates a new row, preserving history** — see §5.

## 11. Known Limitations

- **No exactly-once side effects** — see §6. Acknowledged, not hidden.
- **No distributed queue.** At a scale where a single Postgres instance's connection count or write
  throughput becomes the bottleneck for claiming, a real queue (Redis Streams, SQS, RabbitMQ) would be
  the next step — not needed at this scale, and adding one now would be solving a problem that doesn't
  exist yet.
- **Simplified scheduling.** No cron expressions, no per-job timezone, no "run at this exact wall-clock
  time" one-off scheduling beyond Run Now.
- **Limited observability.** Structured logs exist (execution id, job id, worker id, attempt, event,
  duration) but there's no metrics/tracing backend wired up — fine for reading one process's logs, not
  for diagnosing behavior across a fleet at 3am.
- **No notifications.** Nothing pages/emails/Slacks anyone when a job fails; a human has to look at the
  dashboard.
- **Per-attempt detail is partially lossy.** `job_executions` stores only the *latest* attempt's
  timing/response/error on a row that's still cycling through retries (matching the assignment's flat
  schema) — you always know how many attempts happened and why the most recent one failed, but not the
  full per-attempt response history mid-retry-cycle.
- **SSRF guard is best-effort**, not a complete solution — see §9.

## 12. What I Would Improve With More Time

- A real queue/broker if throughput ever demanded it, with the claim abstraction (`IExecutionClaimService`)
  already isolated enough to swap the implementation without touching the worker loop or the API.
- Stronger leases (a periodic heartbeat from the worker while running, instead of a single stale
  timestamp check) so the stale threshold could be tuned much lower without false-positive recovery of a
  merely-slow-but-alive execution.
- Metrics (Prometheus) and tracing (OpenTelemetry) across API + worker.
- Execution cancellation (a "Cancel" button for a PENDING/RUNNING execution).
- Richer scheduling (real cron expressions, per-job concurrency limits beyond "one at a time").
- A secrets manager for job-configured Authorization headers instead of storing them as plain job
  config (fine for a take-home, not for a multi-tenant production system).
- Webhook signing for jobs that need to prove requests came from JobForge.
- A full SSRF-prevention posture (egress proxy/allowlist) if this were actually going to run arbitrary
  users' URLs in production.
