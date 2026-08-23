# Secure Statement Delivery — Architecture & Design Walkthrough

A deep, "why is it written this way" tour of the codebase, structured for an interview
discussion. Each section states **what** the code does, **why** it's built that way, the
**alternatives** considered, and the **trade-offs** accepted.

> **Elevator pitch:** A production .NET 10 fintech API that ingests bank statements from multiple
> sources, encrypts each PDF with the customer's South African ID as the open password, stores them
> in S3, and delivers them via short-lived, single-use, IP-bound download links — built as a
> **modular monolith** using **Clean Architecture + DDD + CQRS + Vertical Slices**, with correctness
> (consistency, idempotency, auditability) as the first-class concern.

---

## Table of contents

1. [Architecture: the big picture and why](#1-architecture)
2. [Cross-cutting building blocks](#2-cross-cutting-building-blocks)
3. [Registration: the Keycloak ↔ DB consistency saga](#3-registration-the-keycloak--db-consistency-saga)
4. [Domain invariants](#4-domain-invariants)
5. [The transactional outbox](#5-the-transactional-outbox)
6. [Statement ingestion: many adapters, one funnel](#6-statement-ingestion)
7. [Idempotency & de-duplication (the three layers)](#7-idempotency--de-duplication)
8. [The file pipeline: validate → scan → encrypt → store](#8-the-file-pipeline)
9. [Big files: upload, download, and concurrency](#9-big-files)
10. [Security](#10-security)
11. [Infrastructure concerns](#11-infrastructure-concerns)
12. [Observability](#12-observability)
13. [Testing strategy](#13-testing-strategy)
14. [Appendix: likely interview questions](#14-appendix-likely-interview-questions)

---

## 1. Architecture

### What
Five projects with a strictly inward dependency flow:

| Project | Role |
|---|---|
| `Domain` + `SharedKernel` | Entities, invariants, domain events, `Result`/`Error` — no framework/DB refs |
| `Application` | Use-case orchestration + **ports** (`IApplicationDbContext`, `IFileStorageService`, …) |
| `Infrastructure` | **Adapters** — EF `ApplicationDbContext`, `KeycloakClient`, `S3FileStorageService`, outbox |
| `Web.Api` | Presentation — **vertical slices** (`Features/Statements/Upload/{Endpoint,Command,Handler,Validator}`) |

### Why Clean Architecture (and not the alternatives)
- **The business rules are the asset; infrastructure is replaceable.** The crown jewels (SA-ID-as-PDF-password,
  one-live-statement-per-period, single-use tokens, Keycloak↔DB consistency) are long-lived; Postgres, S3,
  Keycloak, ClamAV are swappable details. The dependency rule keeps the rules independent of those choices —
  proven by the fact that storage has both a `Local` and an `S3` adapter behind one port.
- **Testability of logic without infrastructure.** Handlers depend on `IApplicationDbContext`, not concrete EF,
  so domain and use cases are unit-testable with no DB/cloud.
- **Enforced, not hoped-for.** `ArchitectureTests/LayerTests.cs` fails the build if `Domain` references
  `Application`/`Infrastructure`/`Web.Api`, or `Application` references `Infrastructure`, etc. It's a CI gate.
- **vs N-tier:** N-tier points dependencies *downward* (UI→BLL→DAL→DB), so business logic depends on the data
  layer and the DB leaks upward. Clean Architecture inverts exactly that.
- **vs Transaction Script / anemic model:** fine for CRUD, but this domain has real invariants and race-sensitive
  rules — a rich domain (DDD) models those better.
- **vs pure Vertical Slice:** great feature cohesion (used *inside* `Web.Api`), but with no shared core it tends to
  duplicate business rules. The hybrid keeps one `Domain` **and** per-feature cohesion.

### Why a modular monolith, not microservices
- **No forcing function.** One cohesive domain, one team, tightly-related aggregates sharing a transactional DB.
- **Microservices would attack consistency.** The design leans on cheap **ACID** guarantees (a partial unique
  index for "one statement per period", `FOR UPDATE SKIP LOCKED` for single-use tokens, statement + audit in one
  transaction). Splitting aggregates turns each into a saga/eventual-consistency problem — and we already saw how
  much care *one* cross-system boundary (Keycloak↔DB) takes. For a fintech app that must be auditable and
  consistent, that's a large, risky increase in accidental complexity for no current benefit.
- **The scaling wins are already there without it:** stateless replicas, `SKIP LOCKED` for multi-instance
  coordination, presigned S3 downloads that offload egress. Identity is *already* a separate service (Keycloak).
- **It's built to decompose later:** the outbox is "the natural seam to publish to Kafka"; ingestion is already
  queue-decoupled (`IStatementIngestionSource`). Extraction, not rewrite, when a real driver appears.

**Trade-off:** more indirection/boilerplate (interfaces, a handler per use case). Justified by the security and
consistency requirements; would be over-engineering for simple CRUD.

---

## 2. Cross-cutting building blocks

### The `Result<T>` pattern
```csharp
public static implicit operator Result<TValue>(TValue? value) =>
    value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
```
- **Why:** expected/domain failures are values, not exceptions — handlers return `Result.Failure(error)` and the
  endpoint maps it to a ProblemDetails status. Exceptions are reserved for *genuinely unexpected* faults.
- **Convention:** we standardized on **explicit `Result.Success(x)`** (not the implicit conversion) so success and
  failure returns are visually symmetric, and to sidestep the implicit operator's hidden `null → Failure` behavior
  on reference types.

### `IEndpoint` + route groups
- Endpoints are discovered by reflection (`AddEndpoints`) and mapped via `MapEndpoints`. Each is a vertical slice
  (endpoint + command + handler + validator colocated).
- Shared cross-cutting config is centralized: `MapAuthGroup()` (a `RouteGroupBuilder`) applies the `auth/` prefix +
  `Tags.Auth` + `AllowAnonymous` + rate-limit once, so `login`/`register`/`refresh` each declare only their verb.

### CQRS handlers
- `ICommandHandler<TCommand,TResult>` / `IQueryHandler` separate writes from reads. Commands mutate aggregates and
  raise domain events; queries are read models (some project straight to DTOs).

### Data access: `IApplicationDbContext`, not a repository
- **Why not a repository:** EF's `DbSet<T>` **is** a repository and `DbContext` **is** a Unit of Work. `IApplicationDbContext`
  (owned by `Application`, exposing only `DbSet`s + `SaveChangesAsync`) already gives dependency inversion and
  testability. Wrapping it in a repository for trivial writes is pure ceremony.
- **When a repository *would* earn its place:** to centralize repeated query logic or enforce aggregate-load
  invariants — introduced *selectively*, not as a blanket rule.

---

## 3. Registration: the Keycloak ↔ DB consistency saga

The single most subtle piece. `RegisterUserCommandHandler` provisions an identity in **Keycloak** (HTTP) and a
mirror row in **Postgres** — two systems with **no shared transaction**.

### The invariant we chose
> **A local user must never exist without a Keycloak identity** (`local ⟹ Keycloak`).

The user explicitly prioritized this over the reverse (protecting the local-only SA ID). Rationale: a local "ghost"
with no way to log in is the worse failure for this system.

### How the code enforces it
1. **Keycloak-first ordering** — the local write is only reached once the identity is confirmed.
2. **Confirmed-absent compensation** — on a local save failure, we delete the Keycloak identity **only if we can
   positively confirm the local row never persisted**:
   ```csharp
   catch (Exception saveEx)
   {
       if (await LocalUserIsConfirmedAbsentAsync(user.Id, saveEx))  // re-query the DB
           await TryDeleteKeycloakUserAsync(user.Id);               // safe to roll back
       throw;
   }
   ```
   **Why:** the naive "always delete on any exception" is a trap — an *ambiguous* commit (row persisted, ack lost)
   would delete a valid identity and create exactly the ghost we forbid. We only compensate on proof-of-absence;
   any doubt keeps Keycloak.
3. **Idempotent retry** — on a Keycloak `409` (email exists), `ReconcileOrphanedRegistrationAsync` distinguishes a
   genuine duplicate from an orphan left by a prior failed attempt, and **completes the missing local half** so a
   resubmit converges instead of dead-ending.

### "Why so many try/catch blocks?"
Each catch is a **distinct, necessary decision**, not duplication: (1) translate the Keycloak domain error / reconcile,
(2) confirmed-absent compensation, (3) concurrent-reconcile race → idempotent success, (4) verify-query failure → fail
safe (keep Keycloak), (5) best-effort delete → swallow+log so it can't mask the original error. They're already
extracted into named helpers so `Handle` reads linearly. The count can't drop without losing behavior — it's the
essential complexity of a two-system write.

**Honest limits:** true atomicity is impossible across an HTTP IdP and a DB. We buy *convergence* (retry + reconcile),
not instant atomicity. The residual orphan (Keycloak-without-local) is benign and self-heals on retry.

---

## 4. Domain invariants

- **SA ID is required and validated in the domain.** `User.Create` returns `Result<User>` and validates the ID
  (13 digits, valid date of birth, Luhn) so an invalid value can't be persisted from *any* caller — not just the
  API validator. The column is `NOT NULL` (a migration with a **fail-fast NULL-guard** that refuses to backfill
  legacy nulls with an invalid empty string).
- **One live statement per (customer, period)** — a **partial unique index** (`WHERE status <> 2`) is the hard
  guard; the handler's pre-check just gives a friendly 409 first. The index still holds under a concurrent race.
- **Single-use download tokens** — consumed atomically (below).
- **Value validation lives in the domain** (`SouthAfricanIdValidator`) so it's enforced regardless of entry path.

---

## 5. The transactional outbox

Domain events are written to an `outbox_messages` table **in the same transaction** as the state change
(`ApplicationDbContext.SaveChangesAsync` converts raised events to rows), then dispatched asynchronously — so a
crash after commit can never lose an event.

### `OutboxProcessor` design decisions
- **`BackgroundService`, not raw `IHostedService`:** `BackgroundService` *is* an `IHostedService` for the
  long-running-loop case — it starts `ExecuteAsync` without blocking startup and wires the `stoppingToken` to
  graceful shutdown. Hand-rolling `IHostedService` would be the same thing with more footguns.
- **`BackgroundService`, not Quartz:** the schedule is a trivial fixed-interval poll (`PeriodicTimer`), not a cron;
  and multi-replica coordination is solved *at the data layer* — Quartz's clustered scheduler runs a job on **one**
  node, whereas the outbox *wants* all replicas draining in parallel, arbitrated per-row by Postgres.
- **Raw SQL, not LINQ, for the claim query:**
  ```sql
  SELECT * FROM public.outbox_messages
  WHERE processed_on_utc IS NULL
  ORDER BY occurred_on_utc
  LIMIT {BatchSize}
  FOR UPDATE SKIP LOCKED
  ```
  `FOR UPDATE SKIP LOCKED` **cannot be expressed in EF LINQ** — it's the row-locking clause that turns the table
  into a concurrent work queue where each message is claimed by exactly one worker and no worker blocks on another.
  It's parameterized (`FromSqlInterpolated`) so it's injection-safe, and it returns tracked entities so the
  mark-processed write uses normal change tracking.
- **`BatchSize`:** bounds memory, lock footprint, transaction duration, and retry blast radius per tick — and, with
  `SKIP LOCKED`, spreads the backlog across replicas. It's configurable to tune throughput vs. resource use.
- **`IExecutionStrategy.ExecuteAsync` wrapping the transaction:** **required**, not optional. Because
  `EnableRetryOnFailure` is configured on the DbContext, EF *forbids* a user-initiated transaction outside an
  execution strategy — it must re-run the whole begin→work→commit as one retriable unit on a transient fault.
- **Per-message try/catch marks poison messages processed-with-error** so one bad message can't wedge the queue.

---

## 6. Statement ingestion

Three producers, **one funnel** (`UploadStatementCommand`):
- **Manual** admin multipart upload.
- **M2M push** (`/statements/ingest`) — a Keycloak service account (`client_credentials`), authorized purely on a
  realm role off the token (it has no domain `User`), attributed to a reserved `StatementIngestionService` principal.
- **M2M pull** — `StatementIngestionWorker` long-polls `IStatementIngestionSource` (S3-event-on-SQS); a message is
  acknowledged **only after** the statement is durably created, so a failed message is redelivered and eventually
  dead-lettered — safe because the funnel is idempotent.

**Why one funnel:** validation, scanning, encryption, storage, and auditing must be identical regardless of source.
The actor is passed explicitly (`UploadedByPrincipalId`) so the handler never reaches for an HTTP user context —
a machine path has none.

---

## 7. Idempotency & de-duplication

Three complementary guards, each catching a different case with **explicit, known boundaries**:

| Guard | Catches | Boundary / why |
|---|---|---|
| `(CustomerId, DocumentId)` | one *source* redelivering its own document (at-least-once) | source-scoped id; different sources never share it |
| `(CustomerId, Period, ContentHash)` | the *same file bytes*, same period, **any channel** | byte-identical only; **scoped to period** to avoid false merges |
| `(CustomerId, Period)` active-unique | one **live** statement per period (business rule) | period is the statement's identity |

### Why `DocumentId` is a caller-supplied token
It's the source system's stable, immutable reference for the document — **not** generated by us. It provides
*per-source idempotency* (M2M redelivery), not cross-channel dedup, because M2M and manual live in different id
namespaces. It's deliberately **filename-independent** (the filename is discarded).

### Why the content hash, and why period-scoped
- **Why add it:** the only truly channel-, name-, and source-independent identity is one **derived from the file
  itself** — SHA-256 of the **plaintext** bytes (the ciphertext is non-deterministic, so we hash pre-encryption).
- **Why period-scoped:** a period-*independent* content hash would falsely merge two legitimately-different but
  byte-identical statements across periods (e.g. "no-activity" months). For a banking system, **losing a statement
  is worse than a rare duplicate**, so we scope the hash to `(CustomerId, Period)`: it dedups the same file
  re-delivered for the same period via any channel, and **never** merges different periods. The check runs *before*
  the per-period conflict check so a true redelivery replays (idempotent success) instead of returning a 409.
- **Boundary it can't cross (by design):** the *same statement re-rendered to different bytes* — no purely-derived
  identity exists for that; it falls back to `(Customer, Period)`.

Each guard has a **pre-check** (friendly early return) *and* a **unique index** (hard guard against a concurrent
race, resolved in the `DbUpdateException` path).

---

## 8. The file pipeline

Order: **validate content-type signature → malware scan → content hash → PDF-encrypt → store → create + audit.**

- **Signature validation, not just extension/Content-Type** (`IFileTypeValidator`): both can be faked, so we check
  the leading magic bytes against the *declared* content type. It's a registry keyed by content type, so adding a
  format is a one-line map entry — format *validation* is extensible, even though the PDF-specific steps are not.
- **Malware scan on the plaintext** (`IFileContentScanner` → ClamAV), **fail-closed**: an unreachable scanner blocks
  the upload (500), never waves it through.
- **PDF encryption** (`IPdfProtector` → PdfSharp): AES with the customer's SA ID as the **open password**, a random
  owner password that's discarded. Opening the PDF here also rejects a structurally broken file. This is *the* core
  security feature — and the reason the pipeline is PDF-specific.
- **Canonical filename**: every path stores `Statement_{YYYY-MM}.pdf`, derived from the validated period — the
  caller's filename is **intentionally discarded** on *all four* paths (a misleading/malicious name can't influence
  storage). `S3FileStorageService` additionally sanitizes and guards against path traversal.

---

## 9. Big files

### Upload
- **Streaming end-to-end** — `OpenReadStream` → ClamAV stream → protect stream → `TransferUtility` (which
  auto-switches to **S3 multipart** above ~16 MB: parallel, resumable, required >5 GB). Nothing loads the whole file
  as a `byte[]` on the hot path (the one exception is consolidated multi-month merges — bounded and deliberate).
- **Resumable/TUS** for large or flaky connections — chunked to a shared RWX store, resume from the last byte, live
  progress via SSE that reads from the *shared* store so it works on any pod.

### Download — presigned direct-from-S3
```csharp
return response.RedirectUri is not null
    ? Results.Redirect(presignedUrl, permanent: false)              // API transfers ZERO bytes
    : Results.Stream(fileStream, ..., enableRangeProcessing: true); // fallback streams through API
```
A **302 to a short-lived presigned S3 URL** — the bytes go straight from S3 to the user, never through the API. S3
supports HTTP **Range**, so browsers/download managers resume, parallelize, and stream to disk. The fallback (local
provider) streams with range support. So download scales to large files *by design*.

### The honest ceiling
"Big" currently means **up to 50 MB, robustly** — enforced at five layers (Kestrel, FormOptions, domain validator,
Nginx ingress, ClamAV). True multi-GB isn't supported yet: the blocker is the **in-memory PDF-password encryption**
(PdfSharp loads the whole doc), which requires moving scan+encrypt+store to async background processing *and* a
security-model decision (multi-GB files can't carry the in-PDF open-password).

### "Click download 3× → 3 files?"
- **Single-use token:** only **1** succeeds — atomic consume `... WHERE Id == token && !IsUsed`; the other two get
  `TokenAlreadyUsed`.
- **Multi-use / in-app JWT:** all 3 succeed → 3 presigned redirects → 3 files.
- **Efficiency:** server-side it's cheap (302 + audit insert, zero file bytes), but it wastes **3× S3 egress** and
  user bandwidth. The right fix is **client-side debounce**; **single-use tokens** give a hard server-side guarantee.

---

## 10. Security

- **Download tokens** are our own HS256 JWTs (not Keycloak's): **single-use** (atomic consume), **expiry**,
  **IP-binding** (download must originate from the issuing IP), and **tamper defense** — the statement served is the
  one persisted with the token, never the value from the (signed) claim; a mismatch is rejected. Every access is
  audited.
- **Field encryption at rest** — the SA ID is AES-GCM encrypted via an EF value converter. The converter is supplied
  through `DbContextOptions` (`UseFieldEncryption`) rather than constructor injection so the context stays
  **pooling-eligible** (single options-only constructor required by `AddDbContextPool`).
- **AuthN/AuthZ** — Keycloak JWTs, RS256 pinned (prevents algorithm-confusion), permission-based policies; the M2M
  ingest policy authorizes off a **realm role directly on the token** (bypassing the per-user active check, since a
  service account has no `User`).
- **TLS terminates at the ingress**, so `UseForwardedHeaders` runs before anything that reads the client IP
  (rate limiter, IP-bound tokens) — otherwise every request would look like it came from the ingress pod.

---

## 11. Infrastructure concerns

- **Typed `HttpClient` (`AddHttpClient<IKeycloakClient, KeycloakClient>`)** — injecting `HttpClient` here *is* using
  `IHttpClientFactory` (the typed-client pattern), so you get handler pooling + rotation (no socket exhaustion, no
  stale DNS) and the standard resilience handler from `ServiceDefaults`. It's transient, which is why...
- **...the admin token is cached in a singleton** (`KeycloakAdminTokenCache`), not on the client — every admin op
  previously did a fresh master-realm login. The cache refreshes ~30 s before expiry with a `SemaphoreSlim` to
  collapse concurrent refreshes (no stampede) and a lock-free fast path (`volatile` token + `Interlocked` expiry).
- **EF migrations** run via a dedicated `--migrate` job in prod (advisory-locked, once per release) and on startup in
  dev. The `MakeSouthAfricanIdNumberRequired` migration carries a **fail-fast SQL guard** that aborts with a clear
  message rather than silently backfilling legacy NULLs — deliberately chosen over EF's default `defaultValue: ""`,
  which would have written an invalid, unencrypted empty string.
- **`SendWithBearerAsync` helper** centralizes the "create request → attach bearer → send" pattern that recurred
  across five Keycloak calls (DRY where it's real duplication of *logic*, not just repeated arguments).

---

## 12. Observability

- **Serilog + OpenTelemetry.** The Aspire dashboard's structured-logs tab was empty because `UseSerilog` defaulted
  to `writeToProviders: false` — Serilog owned the pipeline and never forwarded to the OTel logging provider. Fixed
  with `writeToProviders: true`, so events flow to the OTLP exporter that `AddServiceDefaults` already wired. (Traces
  and metrics worked because they bypass the logging pipeline — the tell that pinpointed the bug.)
- **Structured JSON to stdout** in production (`CompactJsonFormatter`) for k8s log aggregation; `FromLogContext`
  enricher carries correlation ids. We *dropped* `WithMachineName`/`WithThreadId` — redundant in a containerized +
  OTel stack (pod identity comes from resource attributes; thread id is meaningless under async).
- **Metrics** (`StatementMetrics`) exposed via a Prometheus endpoint alongside the OTLP exporter; health checks split
  liveness (`/alive`) from readiness (`/health` with PostgreSQL + Redis).

---

## 13. Testing strategy

- **`ArchitectureTests`** — enforce the dependency rule (the layers can't leak) via `NetArchTest`. Design integrity
  is a build gate.
- **`Domain.UnitTests`** — invariants (SA ID validation, period format, revoke rules) with no infrastructure.
- **`Infrastructure.UnitTests`** — the tricky stateless pieces in isolation: `FileTypeValidator`, `Sha256ContentHasher`,
  `KeycloakAdminTokenCache` (including a deterministic 32-way concurrency test proving one login under a stampede),
  field encryption, cache round-trips.
- **`IntegrationTests`** — the ingestion funnel end-to-end via the shared processor (real DB via the test factory's
  `EnsureCreated`), proving: idempotent redelivery, filename-independence, and content-hash dedup (**same period →
  merged; different periods → kept separate**).

**Why this split:** fast domain feedback, isolated verification of race-sensitive infra, and end-to-end proof of the
consistency guarantees — without needing Keycloak/cloud for most of it.

---

## 14. Appendix: likely interview questions

- **Why Clean Architecture over layered/microservices?** → §1. Business rules are the durable asset; consistency is
  cheap under one DB; seams exist to decompose later.
- **How do you keep Keycloak and the DB consistent without a distributed transaction?** → §3. Keycloak-first,
  confirmed-absent compensation, idempotent reconcile; convergence not atomicity.
- **Why raw SQL in the outbox?** → §5. `FOR UPDATE SKIP LOCKED` has no LINQ equivalent and is what makes it a
  race-safe multi-replica queue.
- **`BackgroundService` vs `IHostedService` vs Quartz?** → §5.
- **How is de-duplication guaranteed across channels?** → §7. Three layers; content hash is the derived cross-channel
  identity, scoped to period to avoid false merges.
- **How do big files download without killing the API?** → §9. Presigned direct-from-S3 (302); the API transfers
  zero bytes.
- **What stops a download link being reused?** → §10. Single-use tokens consumed atomically, plus expiry + IP-binding
  + tamper checks.
- **Why is `DocumentId` trustworthy?** → §7. It isn't inherently — it's a source contract; the DB is the source of
  truth via the content hash + period identity.
- **What's the one thing you'd still improve?** → Multi-GB support (async processing + a per-format protection
  strategy), and extracting the registration saga behind an interface for unit-test coverage of that critical path.
