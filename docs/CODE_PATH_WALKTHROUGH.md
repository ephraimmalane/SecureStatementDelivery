# Code-Path Walkthrough — Follow a Request End-to-End

A **narrative** trace of each user journey through the codebase, in execution order, naming the files/classes
as they're hit — "you register, and *this* runs, then *that* runs, and here's *why*." Pairs with
[`INTERVIEW_WALKTHROUGH.md`](./INTERVIEW_WALKTHROUGH.md) (topic/decision view),
[`TECH_QA.md`](./TECH_QA.md) (concept Q&A), and [`SEQUENCE_DIAGRAMS.md`](./SEQUENCE_DIAGRAMS.md) (the two hardest
flows visualized).

## Journeys
0. [App startup (orientation)](#0-app-startup)
1. [Register a customer](#1-register-a-customer)
2. [Log in / refresh](#2-log-in--refresh)
3. [Upload a statement (admin)](#3-upload-a-statement-admin)
4. [Machine ingestion (M2M push & pull)](#4-machine-ingestion)
5. [Generate a download link, then download](#5-generate-a-download-link-then-download)
6. [List statements](#6-list-statements)
7. [Outbox dispatch (background)](#7-outbox-dispatch)

---

## 0. App startup

Everything below runs inside a pipeline set up in **`Program.cs`**:

1. `builder.AddServiceDefaults()` — OpenTelemetry, health checks, service discovery, resilient HttpClient defaults.
2. `builder.Host.UseSerilog(…, writeToProviders: true)` — Serilog *and* forwards to the OTel logging provider.
3. Kestrel `MaxRequestBodySize` + `FormOptions.MultipartBodyLengthLimit` set from `StorageOptions.MaxUploadBytes`.
4. DI wiring — `AddApplication()` / `AddInfrastructure()` / presentation (`AddEndpoints`, Swagger, rate limiting,
   auth). `AddInfrastructure` registers the DbContext pool (with `EnableRetryOnFailure` + field encryption),
   Keycloak (typed client + admin-token cache), storage/scanner/hasher, and the outbox `BackgroundService`.
5. `if (args.Contains("--migrate")) { app.ApplyMigrations(); return; }` — prod migrates via a dedicated job; dev
   migrates on startup inside `if (IsDevelopment())`.
6. Middleware order matters: `UseForwardedHeaders()` **first** (so client IP is correct before the rate limiter
   and IP-bound tokens), then rate limiter, auth, request-context logging, `UseSerilogRequestLogging`, then
   `app.MapEndpoints()` discovers every `IEndpoint` by reflection and maps it.

So when a request arrives, it passes forwarded-headers → rate limit → auth → logging → the matched endpoint.

---

## 1. Register a customer

**Entry:** `POST /auth/register`

1. **`RegisterEndpoint.MapEndpoint`** — maps onto the shared `app.MapAuthGroup()` route group, which applies the
   `auth/` prefix + `Tags.Auth` + `AllowAnonymous` + the `"auth"` rate-limit **once**; the endpoint just declares
   `group.MapPost("register", …)`. The endpoint's `Request` record (email, names, password, SA ID) is bound and
   redacts secrets in `ToString()`.
2. The endpoint builds a **`RegisterUserCommand`** and calls `ICommandHandler<RegisterUserCommand, Guid>`.
   The command first passes through **validation** (`RegisterUserCommandValidator` — non-empty names/email,
   password ≥ 8, and a full SA-ID check: 13 digits, valid DOB, Luhn) before the real handler runs.
3. **`RegisterUserCommandHandler`** (the two-system saga — see `SEQUENCE_DIAGRAMS.md §1`):
   - Calls **`KeycloakClient.RegisterUserAsync`**. Inside: `GetMasterAdminTokenAsync` (served from the singleton
     **`KeycloakAdminTokenCache`**, not a fresh login), then `SendWithBearerAsync` POSTs the new user and
     `AssignRealmRoleAsync` gives it the `customer` role. Returns the Keycloak `sub` UUID.
   - On a Keycloak `409`, it runs **`ReconcileOrphanedRegistrationAsync`** (genuine duplicate vs. completing an
     orphan from a prior failed attempt).
   - Calls **`User.Create(keycloakId, …, saId)`** — the domain factory re-validates the SA ID and returns
     `Result<User>` (Id == the Keycloak `sub`, so domain ids match JWT claims).
   - `context.Users.Add(user)` then `SaveChangesAsync`. **Compensation** deletes the Keycloak identity *only if*
     the local row is confirmed absent.
4. **`ApplicationDbContext.SaveChangesAsync`** does one more thing: `AddDomainEventsAsOutboxMessages()` converts
   the `UserRegisteredDomainEvent` raised in `User.Create` into an **outbox row in the same transaction** — so the
   event can't be lost.
5. The handler returns `Result.Success(user.Id)`; **`RegisterEndpoint`** maps it to `Results.Created(...)`.
6. **Later, asynchronously:** the **`OutboxProcessor`** (journey 7) picks up the `UserRegisteredDomainEvent` and
   dispatches it to **`UserRegisteredDomainEventHandler`** (currently logs; the integration point for a welcome
   email, etc.).

---

## 2. Log in / refresh

**Entry:** `POST /auth/login` (and `POST /auth/refresh`)

1. **`LoginEndpoint`** (same `MapAuthGroup`) → **`LoginUserCommand`** → **`LoginUserCommandHandler`**.
2. The handler calls **`KeycloakClient.LoginAsync`**, which `POST`s the OAuth2 **`password`** grant to Keycloak's
   token endpoint (`PostTokenAsync`) and deserializes a **`KeycloakTokenResponse`**.
3. It returns a **`LoginResponse`** (access token, refresh token, expiry). Failures are translated from
   `KeycloakAuthException`: `401 → InvalidCredentials`, `403 → AccountInactive`.
4. Refresh is identical via `RefreshTokenAsync` (the `refresh_token` grant) → `RefreshTokenCommandHandler`.
   Keycloak owns refresh-token rotation/family invalidation natively.

The returned access token is a Keycloak JWT that authorizes every subsequent authenticated call (validated against
Keycloak's JWKS, RS256 pinned).

---

## 3. Upload a statement (admin)

**Entry:** `POST /statements/upload` (permission `StatementsUpload`)

1. **`UploadStatementEndpoint`** — binds the `IFormFile`, reads the optional **`Document-Id`** header, and the
   form `Request(CustomerId, Period, Description)`. It streams the file via `file.OpenReadStream()` and builds an
   **`UploadStatementCommand`**, recording the acting admin as `UploadedByPrincipalId`.
2. **`UploadStatementCommandHandler.Handle`** runs the dedup + file gauntlet (full trace in
   `SEQUENCE_DIAGRAMS.md §2`):
   - Load the customer's **SA ID** (`ApplicationDbContext.Users`, decrypted by the EF value converter) — it's the
     PDF open password. No active customer ⇒ `CustomerNotFound`.
   - **DocumentId pre-check** → early idempotent return if this source already delivered it.
   - **`IFileTypeValidator`** — magic-byte signature vs declared content type.
   - **`IContentHasher`** — SHA-256 of the plaintext; then the **ContentHash pre-check** scoped to
     `(Customer, Period)` → early idempotent return for the same bytes in the same period, any channel.
   - **Per-period conflict** check → `409 ActiveStatementExistsForPeriod` if a live one exists.
   - **`IFileContentScanner`** (ClamAV, fail-closed) → `MalwareDetected`.
   - **`IPdfProtector`** — AES-encrypt with the SA ID as the open password (also rejects a broken PDF).
   - **`IFileStorageService.StoreAsync`** (`S3FileStorageService` → `TransferUtility`, multipart >16 MB) under the
     canonical name `Statement_{period}.pdf`.
   - **`Statement.Create(…, documentId, contentHash)`** → `Statements.Add` + a `DownloadAuditLog` →
     `SaveChangesAsync`, with a `DbUpdateException` block that cleans up the S3 object and resolves which unique
     index a concurrent writer won.
3. `SaveChangesAsync` writes the `StatementUploadedDomainEvent` to the outbox in the same transaction.
4. **`UploadStatementEndpoint`** maps the result to `Results.Created`. Asynchronously, the outbox dispatches the
   `StatementUploadedDomainEvent` to its handler.

---

## 4. Machine ingestion

The bank's generator delivers statements without a human. Both variants **feed the exact same
`UploadStatementCommand` and handler** as §3 — only the adapter differs.

**Push** — `POST /statements/ingest`:
1. **`IngestStatementEndpoint`** — authorized by the **service-account policy** (a Keycloak `client_credentials`
   token carrying the `statement-ingest` realm role, checked directly off the token — no domain `User`).
2. Requires a **`Document-Id`** header (ingestion is at-least-once). Builds the command with the reserved
   `SystemPrincipals.StatementIngestionService` actor → same handler as §3.

**Pull** — background worker:
1. **`StatementIngestionWorker`** (a `BackgroundService`) long-polls **`IStatementIngestionSource`**
   (`SqsStatementIngestionSource` — S3-event-on-SQS; metadata `customerid`/`period`/`documentid`/`filename`).
2. Each **`StatementIngestionMessage`** is handed to **`StatementIngestionProcessor.ProcessAsync`**, which builds
   the same `UploadStatementCommand` → same handler.
3. The message is **acknowledged only after** the statement is durably created (`AcknowledgeAsync` deletes it);
   a failure leaves it for redelivery and eventual dead-lettering — safe because the funnel is idempotent.

---

## 5. Generate a download link, then download

**Step A — mint a link:** `POST /statements/{id}/download-tokens` (permission)
1. **`GenerateDownloadLinkEndpoint`** → **`GenerateDownloadLinkCommand`** → **`GenerateDownloadLinkCommandHandler`**.
2. The handler loads the statement, checks ownership (admin, or the statement's own customer) and that it's active,
   then calls **`IDownloadTokenService.GenerateToken`** (our own HS256 JWT) and **`DownloadToken.Create`** (id,
   statement id, requesting user, hash, expiry, single-use flag, optional IP). It adds the token row + a
   `DownloadLinkGenerated` audit log and saves. Returns the **raw token** + expiry.

**Step B — download:** `GET /statements/download?token=…` (`AllowAnonymous`, rate-limited)
1. **`DownloadStatementEndpoint`** captures the client IP + user-agent and builds a **`DownloadStatementQuery`**.
2. **`DownloadStatementQueryHandler`**:
   - `ValidateToken` (JWT signature/claims). Load the persisted **`DownloadToken`**.
   - **Tamper defense:** the served statement is the one persisted with the token, never the claim value — a
     mismatch is rejected. Then expiry, single-use-already-used, and **IP-binding** checks (IP mismatch is audited
     and denied).
   - **Single-use consume** — an atomic conditional update (`… WHERE Id == token && !IsUsed`); of N concurrent
     requests, exactly one wins.
   - Audit `StatementDownloaded`, then **`IFileStorageService.GeneratePresignedDownloadUriAsync`** →
     `StatementFileResponse(presignedUri)`.
3. Back in **`DownloadStatementEndpoint`**: if a presigned URI exists → **`Results.Redirect(url, permanent:false)`**
   (a **302** — the bytes stream straight from S3, the API transfers zero payload). Otherwise (local provider) →
   `Results.Stream(..., enableRangeProcessing: true)`.

**In-app variant** — `GET /statements/{id}/content` (customer's JWT is the credential):
`DownloadInAppEndpoint` → `DownloadInAppQueryHandler` (ownership check + audit + presigned redirect), no token.

---

## 6. List statements

**Entry:** `GET /statements` (permission `StatementsReadOwn`)

1. **`ListStatementsEndpoint`** — binds `customerId`, **`range`** (the preset enum), `periodFrom`/`periodTo`,
   paging (documented via `[Description]` attributes + `.WithSummary`/`.WithDescription`). Builds a
   **`GetStatementsQuery`**.
2. **`GetStatementsQueryHandler`**:
   - Ownership scoping — a non-admin sees only their own statements; an admin may target a `customerId`.
   - **`ResolvePeriodWindow`** turns a preset `range` (LastMonth/Last3/6/12) into an inclusive `YYYY-MM` window of
     the last N *completed* months, or uses `periodFrom`/`periodTo` for `Custom`/unspecified.
   - A lexical `string.Compare` range filter (valid because periods are canonical `YYYY-MM`), each bound validated.
   - Paginated projection (with `Customer.FullName`) → **`PagedStatementResponse`**.

---

## 7. Outbox dispatch

Runs continuously, independent of any request.

1. **`OutboxProcessor`** (a `BackgroundService`) wakes on a `PeriodicTimer` every `PollIntervalSeconds`.
2. **`ProcessBatchAsync`** opens a fresh DI scope, gets the `ApplicationDbContext` + `IDomainEventsDispatcher`, and
   wraps the work in **`Database.CreateExecutionStrategy().ExecuteAsync`** (required because `EnableRetryOnFailure`
   is on and this is a manual transaction).
3. Inside a transaction, it **claims a batch** with raw SQL — `SELECT … WHERE processed_on_utc IS NULL ORDER BY
   occurred_on_utc LIMIT {BatchSize} FOR UPDATE SKIP LOCKED` — so every replica drains different rows with no
   blocking.
4. For each row: `Deserialize` the event type, `dispatcher.DispatchAsync` to its `IDomainEventHandler`s, set
   `ProcessedOnUtc`. A per-message try/catch marks a poison message processed-with-error so it can't wedge the
   queue.
5. `SaveChangesAsync` + `CommitAsync`. On a transient fault the whole batch is retried as one unit.

---

## How to use this in an interview

- Open with **journey 3 (upload)** — it touches the most concepts (validation, dedup layers, scan, encryption,
  storage, outbox) in one story.
- Use **journey 1 (register)** to show you understand distributed consistency without hand-waving.
- Use **journey 5 (download)** to show the security model and the presigned-offload performance decision.
- When asked "why," jump to the matching section in `TECH_QA.md`; when asked "show me," jump to
  `SEQUENCE_DIAGRAMS.md`.
