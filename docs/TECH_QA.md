# Tech Concepts Q&A — Choices & Trade-offs

Interview-style Q&A covering the **technology concepts** in this codebase and, for each, **why it was
chosen over the alternatives**. Companion to [`INTERVIEW_WALKTHROUGH.md`](./INTERVIEW_WALKTHROUGH.md)
(which walks the code); this one drills the *concepts* and *comparisons*.

Format: **Q** → short answer → **Chosen over** (the alternative(s) and why not).

## Contents
- [A. Architecture & design](#a-architecture--design)
- [B. Consistency, sagas & the outbox](#b-consistency-sagas--the-outbox)
- [C. EF Core & data access](#c-ef-core--data-access)
- [D. Concurrency & background work](#d-concurrency--background-work)
- [E. Idempotency & de-duplication](#e-idempotency--de-duplication)
- [F. Files, streaming & storage](#f-files-streaming--storage)
- [G. Security & identity](#g-security--identity)
- [H. HTTP, resilience & caching](#h-http-resilience--caching)
- [I. Observability](#i-observability)
- [J. Testing](#j-testing)

---

## A. Architecture & design

**Q: What is Clean Architecture and why use it here?**
A: Concentric layers with dependencies pointing **inward** toward a framework-free domain. Business rules
(the durable asset) don't depend on DB/HTTP/cloud (the volatile details).
**Chosen over N-tier:** N-tier points dependencies *downward* (UI→BLL→DAL→DB), so business logic depends on
the data layer and the DB leaks upward — the opposite of what you want for a rule-rich domain.

**Q: Isn't Clean Architecture the same as Hexagonal / Ports & Adapters?**
A: Practically yes — Clean Architecture *is* ports-and-adapters with prescribed concentric layers. Our
`IFileStorageService` (S3/Local), `IStatementIngestionSource` (SQS), `IKeycloakClient` **are** ports with
adapters. Clean Architecture just formalizes the layering around them.

**Q: DDD vs an anemic model + service layer?**
A: DDD (rich aggregates with behavior/invariants).
**Chosen over anemic/Transaction Script:** the domain has real invariants and race-sensitive rules
(one-live-statement-per-period, single-use tokens, the Keycloak↔DB saga). Procedural transaction scripts
model those poorly; a rich domain enforces them at the source (`User.Create` validates the SA ID so *no*
caller can persist an invalid one).

**Q: CQRS — full event-sourced CQRS?**
A: No — lightweight CQRS: separate `ICommandHandler`/`IQueryHandler`, same database. Commands mutate
aggregates + raise events; queries project to DTOs.
**Chosen over event sourcing:** we get the read/write separation and testability without the operational
cost, eventual-consistency complexity, and rebuild/versioning burden of a full event store.

**Q: Why a modular monolith and not microservices?**
A: One cohesive domain, one team, tightly-related aggregates sharing an ACID database. Microservices buy
independent deploy/scale/fault-isolation — none of which has a forcing function yet.
**Chosen over microservices:** splitting aggregates turns cheap DB-enforced guarantees (unique indexes,
`SKIP LOCKED`, statement+audit in one transaction) into sagas + eventual consistency. For a fintech app that
must be consistent and auditable, that's a large, risky increase in *accidental* complexity for no benefit.
The design leaves **seams** (outbox → Kafka, ingestion adapters) to decompose later — extraction, not rewrite.

**Q: Vertical Slice Architecture — how does it coexist with layered Clean Architecture?**
A: Slices are used *inside* the presentation layer (`Features/X/{Endpoint,Command,Handler,Validator}`) for
per-feature cohesion; the shared `Domain`/`SharedKernel` prevents rule duplication.
**Chosen over pure VSA:** pure slices with no shared core tend to duplicate/leak business rules across
features.

**Q: `Result<T>` pattern vs throwing exceptions for domain failures?**
A: Return `Result.Failure(error)` for *expected* domain failures; reserve exceptions for genuinely
unexpected faults.
**Chosen over exceptions-for-control-flow:** exceptions are expensive and obscure the happy path; a `Result`
makes the failure a value the endpoint maps to a ProblemDetails status. (We also standardized on explicit
`Result.Success(x)` over the implicit conversion, for symmetry and to avoid the operator's hidden
`null → Failure` behavior on reference types.)

**Q: `IApplicationDbContext` vs the Repository pattern?**
A: Expose an `IApplicationDbContext` (DbSets + `SaveChangesAsync`) owned by the Application layer.
**Chosen over a repository:** EF's `DbSet<T>` *is* a repository and `DbContext` *is* a Unit of Work; wrapping
them adds a forwarding layer with no gain. You already get dependency inversion + testability. A repository is
introduced *selectively* only where it centralizes real query logic or enforces aggregate-load invariants.

---

## B. Consistency, sagas & the outbox

**Q: How do you keep two systems (Keycloak + Postgres) consistent with no distributed transaction?**
A: You can't get true atomicity across an HTTP IdP and a DB (no 2PC over Keycloak's REST API), so you buy
**convergence**: Keycloak-first ordering, **compensation only on proof the local row didn't persist**, and
an **idempotent reconcile** on retry.
**Chosen over 2PC/XA:** unavailable and operationally heavy; **over "just delete Keycloak on any exception":**
that's a trap — an *ambiguous* commit (row persisted, ack lost) would delete a valid identity and create a
local "ghost." We compensate only on confirmed-absence.

**Q: What is the Outbox pattern and why use it?**
A: Write domain events to an `outbox` table **in the same transaction** as the state change, then dispatch
asynchronously. A crash after commit can't lose the event.
**Chosen over publishing directly to a broker inside the handler:** that's a dual-write (DB + broker) with no
atomicity — the classic "committed to DB but the publish failed" inconsistency. The outbox makes the event
part of the DB transaction.

**Q: Saga vs distributed transaction?**
A: A saga is a sequence of local transactions with compensating actions. Our registration is a two-step saga
(provision identity → persist mirror, compensate on failure).
**Chosen over a distributed transaction:** 2PC across heterogeneous systems is fragile and often unsupported;
sagas trade atomicity for availability + explicit compensation.

**Q: At-least-once vs exactly-once delivery?**
A: The outbox + queue gives **at-least-once**; handlers must be **idempotent**. Exactly-once is effectively a
myth across process/network boundaries.
**Chosen deliberately:** idempotent consumers + de-dup keys are simpler and more robust than chasing
exactly-once semantics.

---

## C. EF Core & data access

**Q: Why raw SQL (`FromSqlInterpolated`) for the outbox claim instead of LINQ?**
A: `FOR UPDATE SKIP LOCKED` is a Postgres row-locking clause with **no LINQ equivalent** — EF can't emit it.
It's what turns the table into a concurrent work queue (each row claimed by exactly one worker, no blocking).
**Chosen over LINQ + optimistic concurrency:** plain LINQ would select the same rows on every replica →
contention/duplicate processing; `SKIP LOCKED` avoids the contention entirely rather than detect-and-retry.
Parameterized so it's injection-safe; returns tracked entities so the write side stays normal EF.

**Q: `EnableRetryOnFailure` + a manual transaction — why the `IExecutionStrategy.ExecuteAsync` wrapper?**
A: With a retrying execution strategy, EF **forbids** a user-initiated transaction unless it's wrapped, so it
can re-run the whole begin→work→commit as one retriable unit. Not optional — omit it and every batch throws.
**Chosen over no-retry:** transient faults (failover, deadlocks, blips) are retried automatically instead of
surfacing to callers.

**Q: Pessimistic (`FOR UPDATE`) vs optimistic concurrency here?**
A: Pessimistic for the outbox queue (claim + skip-locked); a mix elsewhere — the single-use token uses a
**conditional update** (`WHERE is_used = false`), which is optimistic-flavored and race-proof (exactly one row
affected of N concurrent requests).
**Chosen per-case:** queues want pessimistic claim to distribute work; a one-shot flag flip is cleanest as a
conditional update.

**Q: Why `AddDbContextPool` and the odd single-constructor `ApplicationDbContext`?**
A: Pooling reuses context instances (perf). It requires the context to have a **single `DbContextOptions`-only
constructor**, which is why the field encryptor is supplied via `DbContextOptions` (`UseFieldEncryption`)
rather than constructor injection.
**Chosen over plain `AddDbContext`:** lower allocation/latency under load, at the cost of that constructor
constraint (worked around cleanly).

**Q: EF value converter for the encrypted SA ID — why?**
A: Encrypt on write / decrypt on read transparently, so the domain stays plaintext and every read/write path
is encrypted consistently — you can't forget to encrypt.
**Chosen over encrypting in the handler:** centralizes it; no code path can bypass it.

**Q: Why a fail-fast SQL guard in the "make SA ID NOT NULL" migration?**
A: EF's default would backfill existing NULLs with `defaultValue: ""` — an invalid, *unencrypted* value. The
guard raises a clear exception instead, forcing a real backfill.
**Chosen over the silent default:** in fintech, silently corrupting a credential-bearing column is worse than
a loud, blocking migration.

---

## D. Concurrency & background work

**Q: `BackgroundService` vs raw `IHostedService`?**
A: `BackgroundService` *is* an `IHostedService` for the long-running-loop case — it starts `ExecuteAsync`
without blocking startup and wires the `stoppingToken` for graceful shutdown.
**Chosen over raw `IHostedService`:** hand-rolling the loop means managing your own task + CTS and risking a
blocked `StartAsync`. Use raw `IHostedService` only for one-shot start/stop hooks, not loops.

**Q: `BackgroundService` + `PeriodicTimer` vs Quartz.NET (or Hangfire)?**
A: The schedule is a trivial fixed-interval poll, and multi-replica coordination is solved *at the data layer*
(`SKIP LOCKED`).
**Chosen over Quartz:** Quartz is a scheduler (cron, persistent job store, **clustered = run-on-one-node**),
which is the opposite of what the outbox wants (all replicas draining in parallel). It'd add a dependency +
DB tables for no benefit. Reach for Quartz/**Hangfire** when you need cron scheduling, misfire policies, or a
managed job catalog with a dashboard — not for a continuous drain.

**Q: How is a cache stampede avoided in `KeycloakAdminTokenCache`?**
A: A `SemaphoreSlim(1,1)` with **double-checked locking**: check the token lock-free, and only one caller does
the refresh while the rest await and then re-read the freshly-cached token.
**Chosen over a plain lock or no guard:** no guard → N concurrent logins on expiry (stampede); a `lock` can't
be held across `await`. `SemaphoreSlim` is the async-safe primitive; the lock-free fast path uses `volatile`
(token) + `Interlocked` (expiry ticks) for correct cross-thread visibility without serializing reads.

**Q: Why cache the Keycloak admin token at all, and why in a singleton?**
A: Every admin op otherwise does a full master-realm `password` login (a round-trip). The client is a
**transient** typed `HttpClient`, so instance caching wouldn't share — the cache must be a **singleton**.
**Chosen over per-call login:** ~one login per token lifetime instead of per operation.

---

## E. Idempotency & de-duplication

**Q: Why three de-dup keys instead of one?**
A: Each catches a different case with an explicit boundary: `DocumentId` = one *source* redelivering;
`ContentHash` (per customer+period) = same *bytes* via any channel; `(Customer, Period)` = one *live*
statement per period.
**Chosen over a single key:** no single identifier covers all cases — assigned ids are source-scoped, content
hashes only catch byte-identical files, and period is the business identity.

**Q: Why is `DocumentId` not enough for cross-channel dedup?**
A: It's a **source-scoped**, caller-supplied token. M2M's ids and a manual upload's ids live in different
namespaces, so they never match across channels.
**Chosen role:** per-source *idempotency* (at-least-once redelivery), not cross-source identity.

**Q: Why a content hash, and why hash the *plaintext*?**
A: A hash **derived from the file** is the only channel-, name-, and source-independent identity. We hash the
**plaintext** because the stored file is AES-encrypted with a **random nonce** — the ciphertext differs every
time, so only the plaintext yields a stable fingerprint.
**Chosen over hashing the stored/encrypted bytes:** those aren't deterministic.

**Q: Why is the content hash scoped to the period? Doesn't that weaken it?**
A: A period-*independent* hash would falsely merge two legitimately-different but byte-identical statements in
different periods (e.g. no-activity months). Scoping to `(Customer, Period)` prevents the false merge while
still deduping the same file re-delivered for the same period via any channel.
**Chosen over period-independent:** for a banking system, **losing a statement is worse than a rare
duplicate**. The residual gap (same statement re-rendered to different bytes) has no derived identity and
falls back to `(Customer, Period)`.

**Q: Could two different files collide on the same hash?**
A: SHA-256 is 256-bit; accidental collision is ~1 in 2²⁵⁶ (effectively zero) and no collision has ever been
produced. Same *size* ≠ same hash — the hash covers every byte with avalanche behavior.
**Chosen SHA-256 over CRC/MD5:** cryptographic strength, negligible collision risk, computed in the same
read pass we already do for scanning.

**Q: Why not content-hash dedup for everything (drop DocumentId/Period)?**
A: Content hashing only recognizes **byte-identical** files; it can't catch a re-rendered "same" statement,
and (scoped to period) it can't catch cross-period re-files. The other two layers cover those.

---

## F. Files, streaming & storage

**Q: How do you handle large uploads without buffering the whole file?**
A: Stream end-to-end (`OpenReadStream` → scan → encrypt → `TransferUtility`), which auto-switches to **S3
multipart** above ~16 MB (parallel, resumable). Plus a **TUS resumable** path for flaky/large connections.
**Chosen over buffering to a `byte[]`/temp file:** bounded memory, resumability, and no full-file
materialization on the hot path.

**Q: How does a big file reach the user's machine?**
A: A **302 redirect to a short-lived presigned S3 URL** — the client downloads **directly from S3**; the API
transfers zero file bytes. S3 supports HTTP **Range**, so resume/parallel/stream-to-disk work.
**Chosen over streaming through the API:** streaming would consume pod memory + bandwidth and risk timeouts;
presigned offloads egress to S3, which is built for it. A local-provider **fallback** streams with
`enableRangeProcessing` for resume support.

**Q: Why validate magic bytes when there's already a Content-Type check?**
A: Extension and `Content-Type` can be faked; the leading **magic bytes** verify the file *is* the declared
type. `IFileTypeValidator` is a content-type→signature registry, so adding a format is a one-line entry.
**Chosen over trusting the header:** defense against spoofed uploads.

**Q: Why is the malware scan "fail-closed"?**
A: An unreachable ClamAV **blocks** the upload (500 "service degraded"), never waves it through.
**Chosen over fail-open:** for a fintech system, letting an unscanned file through on scanner outage is
unacceptable.

**Q: Why encrypt the PDF with the SA ID instead of relying on transport/at-rest encryption?**
A: The **open password** lets the customer open the delivered PDF in any reader with a credential they know
(their ID) — an end-to-end document protection independent of how it's transported/stored.
**Chosen over app-only encryption:** app-level encryption would force download-through-the-app (losing the
presigned-direct-download offload). The trade-off: PDF-password encryption is in-memory (PdfSharp), which caps
practical file size.

**Q: Why discard the caller's filename and canonicalize to `Statement_{YYYY-MM}.pdf`?**
A: Uniform naming across all four upload paths, and a misleading/malicious filename can't influence storage.
The storage layer additionally sanitizes and guards path traversal.
**Chosen over trusting the caller's name:** consistency + security.

---

## G. Security & identity

**Q: How do single-use download links work and why are they race-proof?**
A: A conditional atomic update — `UPDATE ... SET is_used = true WHERE id = @id AND is_used = false`. Of N
concurrent requests, exactly one affects a row (wins); the rest affect 0 rows and are rejected.
**Chosen over "read-then-write":** read-check-then-update has a TOCTOU race; the conditional update is
atomic in the DB.

**Q: What defenses beyond single-use are on a download token?**
A: **Expiry**, **IP-binding** (must originate from the issuing IP), and **tamper defense** — the served
statement is the one persisted with the token, never the value from the (signed) claim; a mismatch is
rejected. Every access is audited.
**Chosen layering:** defense-in-depth — a stolen link is short-lived, IP-bound, and can't be repurposed.

**Q: Why HS256 for download tokens but RS256 pinned for Keycloak JWTs?**
A: Download tokens are our own symmetric secret (we sign and verify) — HS256 is fine. Keycloak JWTs are
verified against its **public** JWKS, so RS256 (asymmetric); we **pin** the algorithm to prevent
algorithm-confusion attacks (e.g. an attacker downgrading to HS256 using the public key as the HMAC secret).
**Chosen deliberately per use case.**

**Q: AES-GCM for field encryption — why GCM?**
A: GCM is **authenticated** encryption (confidentiality + integrity) with a per-value random nonce, so
tampering is detected on decrypt.
**Chosen over AES-CBC:** CBC lacks built-in integrity (needs a separate MAC) and is padding-oracle-prone.

**Q: OAuth2 grants — why `password` for the admin token but `client_credentials` for M2M ingest?**
A: The admin token is a service *user* login (`password` grant, admin-cli); M2M ingest is a **machine** with
no user, so `client_credentials` with a service account carrying a realm role.
**Chosen per actor:** machines shouldn't impersonate users; `client_credentials` is the machine grant.

**Q: Why `UseForwardedHeaders` early in the pipeline?**
A: TLS terminates at the ingress, so the pod sees the ingress IP unless forwarded headers are honored **before**
anything reads the client IP (rate limiter, IP-bound tokens).
**Chosen ordering:** otherwise all requests collapse to one IP — breaking rate-limit partitioning and IP-bound
downloads.

---

## H. HTTP, resilience & caching

**Q: Typed `HttpClient` vs injecting `IHttpClientFactory` and calling `CreateClient`?**
A: Injecting `HttpClient` into a typed client (`AddHttpClient<IKeycloakClient, KeycloakClient>`) **is** using
the factory — you get handler pooling + rotation (no socket exhaustion, no stale DNS) and the shared resilience
handler.
**Chosen over `new HttpClient()`:** that's the classic anti-pattern (socket exhaustion or stale DNS).
**Over injecting `IHttpClientFactory` directly:** only needed for runtime-selected named clients or a singleton
consumer; the typed pattern is less boilerplate for a single well-defined client.

**Q: What does the "standard resilience handler" add?**
A: Retries, circuit breaker, and timeouts by default on every factory client (via `ServiceDefaults`).
**Chosen over hand-rolled Polly per call:** consistent, centrally configured resilience.

---

## I. Observability

**Q: Why did the Aspire structured-logs tab show traces/metrics but no logs?**
A: `UseSerilog` defaulted to `writeToProviders: false`, so Serilog owned the pipeline and never forwarded to
the **OpenTelemetry logging provider** — logs went to Serilog's own sinks only. Traces/metrics appeared because
they bypass the logging pipeline (the tell). Fixed with `writeToProviders: true`.
**Concept:** OTLP export of logs requires the events to reach the MEL/OTel provider; Serilog must be told to
also write to registered providers.

**Q: Why structured JSON logs to stdout in production?**
A: k8s log aggregators (Fluent Bit/Loki/CloudWatch) parse structured logs; `CompactJsonFormatter` to stdout is
the idiomatic target. `FromLogContext` carries correlation ids.
**Chosen over plain text + a file sink:** container stdout + structured JSON is the cloud-native norm; we
dropped `WithMachineName`/`WithThreadId` as redundant (pod identity comes from OTel resource attributes; thread
id is meaningless under async).

**Q: OpenTelemetry vs vendor-specific telemetry?**
A: OTel is vendor-neutral (OTLP) — traces, metrics, logs to any compatible backend (the Aspire dashboard in
dev; a collector in prod), coexisting with a Prometheus scrape endpoint.
**Chosen over a single-vendor SDK:** portability and one instrumentation standard.

---

## J. Testing

**Q: What are Architecture Tests and why have them?**
A: `NetArchTest` assertions that fail the build if the dependency rule is violated (Domain can't reference
Application/Infrastructure, etc.).
**Chosen over convention/code-review only:** boundaries are enforced in CI, not hoped for.

**Q: How do you test race-sensitive infrastructure like the token cache?**
A: A deterministic concurrency test — 32 callers hit `GetTokenAsync` where the fetch holds the lock via
`Task.Delay`, asserting exactly **one** login across all of them.
**Concept:** force the stampede window open with a delay so the assertion is deterministic, not timing-luck.

**Q: Unit vs integration split — where's the line?**
A: Domain/stateless-infra logic → fast unit tests (no DB). The ingestion funnel + dedup guarantees → integration
tests against a real DB (via the test factory), proving idempotent redelivery, filename-independence, and
content-hash dedup (same period → merged; different periods → separate).
**Chosen split:** fast feedback on logic, real proof of the consistency behaviors, minimal cloud/Keycloak
dependence.
