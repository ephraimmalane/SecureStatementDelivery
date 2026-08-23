# Sequence Diagrams

ASCII sequence diagrams for the two most subtle flows, with inline **why** annotations. Companion to
[`INTERVIEW_WALKTHROUGH.md`](./INTERVIEW_WALKTHROUGH.md) (§3 registration, §7–8 upload) and
[`TECH_QA.md`](./TECH_QA.md).

**Legend**
```
──▶   a call / message to a participant        gate   an early-return guard (short-circuits)
◀──   a return                                 alt    a branch; else marks the other arm
// …  the reason ("why")                        RETURN the handler returns here
```

---

## 1. Registration saga — Keycloak ↔ Postgres (no distributed transaction)

`RegisterUserCommandHandler`. Two systems, no shared transaction. The invariant enforced end-to-end is
**"a local user must never exist without a Keycloak identity" (local ⟹ Keycloak)**.

```
Client            RegisterHandler              KeycloakClient            Postgres (EF)
  │                     │                            │                        │
  │  register(email, names, password, saId)          │                        │
  │────────────────────▶│                            │                        │
  │                     │  RegisterUserAsync          │                        │
  │                     │────────────────────────────▶│  create user +         │
  │                     │                            │  assign 'customer' role │
  │                     │                            │                        │
  │  ╔══ alt  Keycloak returns 409 (email already exists) ══════════════════╗ │
  │  ║                  │  KeycloakRegistrationException                     ║ │
  │  ║                  │◀────────────────────────────│                      ║ │
  │  ║                  │  ── ReconcileOrphanedRegistrationAsync ──          ║ │
  │  ║                  │  "email exists locally?" ───────────────────────────▶│
  │  ║                  │◀──────────────────────────────────────────────────── │
  │  ║   ┌ alt local mirror EXISTS → genuine duplicate                       ║ │
  │  ║   │  return Failure(EmailNotUnique)                                   ║ │
  │  ║◀──┤                                                                   ║ │
  │  ║   └ else NO local mirror → orphan left by a prior failed attempt      ║ │
  │  ║      GetUserIdByEmailAsync ─────────────────────▶│  recover orphan id  ║ │
  │  ║      User.Create(recoveredId,…) + Add + SaveChanges ──────────────────▶│
  │  ║◀──── return Success(id)   // completes the missing half; retry converges║ │
  │  ║                  │                            │                        ║ │
  │  ╚═══ RETURN ═══════════════════════════════════════════════════════════╝ │
  │                     │                            │                        │
  │                     │  keycloakId  // new identity confirmed              │
  │                     │◀────────────────────────────│                       │
  │                     │                                                     │
  │                     │  User.Create(keycloakId, names, saId)               │
  │                     │  // validates SA ID: 13 digits, valid DOB, Luhn     │
  │  ╔══ alt  SA ID invalid ═══════════════════════════════════════════════╗ │
  │  ║                  │  TryDeleteKeycloakUserAsync ─▶│  roll back identity  ║ │
  │  ║◀──── Failure(InvalidIdNumber)                  │  // nothing local yet ║ │
  │  ╚═══ RETURN ═══════════════════════════════════════════════════════════╝ │
  │                     │                                                     │
  │                     │  Users.Add(user); SaveChangesAsync ─────────────────▶│
  │                     │                                                     │
  │  ╔══ alt  SaveChanges threw ════════════════════════════════════════════╗ │
  │  ║                  │  LocalUserIsConfirmedAbsentAsync  ──────────────────▶│  re-query
  │  ║                  │◀──────────────────────────────────────────────────── │
  │  ║   ┌ alt row CONFIRMED ABSENT (proof nothing committed)                ║ │
  │  ║   │   TryDeleteKeycloakUserAsync ─▶│  safe compensation                ║ │
  │  ║   │   rethrow                                                          ║ │
  │  ║   └ else row present, OR can't tell (ambiguous commit / DB down)       ║ │
  │  ║       KEEP Keycloak identity   // deleting it here = the ghost we forbid║ │
  │  ║       rethrow                                                          ║ │
  │  ╚════════════════════════════════════════════════════════════════════════╝ │
  │                     │                                                     │
  │  Created(user.Id)   │  return Success(user.Id)                            │
  │◀────────────────────│                                                     │
```

**Why the shape:**
- **Keycloak-first** — we only reach the local write once the identity is confirmed, so a crash before the
  local save can only ever leave a *Keycloak-without-local* orphan (benign, self-heals on retry) — never the
  forbidden local ghost.
- **Confirmed-absent compensation** — the naive "delete Keycloak on any exception" would destroy a valid
  identity when a commit *succeeded but the ack was lost*. We re-query and delete **only on proof of absence**.
- **Idempotent reconcile** — a resubmit after a failed local save no longer dead-ends on 409; it completes the
  missing half. The concurrent-reconcile race is resolved by returning the winner's id.
- **True atomicity is impossible** across an HTTP IdP and a DB; this buys **convergence**, not 2PC.

---

## 2. Upload de-duplication pipeline (one funnel, three guards)

`UploadStatementCommandHandler`. Every source (manual multipart, M2M push, M2M pull, resumable) builds the same
`UploadStatementCommand` and runs this gauntlet. Each **pre-check** is a friendly early return; each is backed by
a **unique index** as the hard guard against a concurrent race.

```
   manual / M2M-push / M2M-pull / resumable
        │   builds UploadStatementCommand(customerId, period, contentType, stream, documentId?)
        ▼
   UploadStatementCommandHandler.Handle
        │
  (1)   ├─ Load SA ID ─────────────────────▶ Postgres  (Users: Id & IsActive → SouthAfricanIdNumber)
        │     gate  null → return CustomerNotFound
        │           // the SA ID is the PDF open password; no customer ⇒ no upload
        │
  (2)   ├─ DocumentId pre-check  [only if command.DocumentId present]
        │     ─────────────────────────────▶ Postgres  (Statements: CustomerId & DocumentId)
        │     gate  found → return Success(existingId)
        │           // per-SOURCE idempotency: same source redelivering its own document (at-least-once)
        │
  (3)   ├─ Validate signature ─────────────▶ IFileTypeValidator  (magic bytes vs declared content-type)
        │     gate  invalid → metrics.UploadRejected("invalid_content"); return InvalidFileContent
        │           // extension / Content-Type can be faked; the leading bytes cannot
        │
  (4)   ├─ Compute content hash ───────────▶ Sha256ContentHasher  (SHA-256 of PLAINTEXT; rewinds stream)
        │           // hash plaintext: the encrypted blob has a random nonce ⇒ non-deterministic
        │
  (5)   ├─ ContentHash pre-check ──────────▶ Postgres  (Statements: CustomerId & Period & ContentHash)
        │     gate  found → return Success(existingId)
        │           // CROSS-CHANNEL identity: same bytes, same period, any channel/name/id
        │           // SCOPED TO PERIOD so byte-identical statements in DIFFERENT periods are NOT merged
        │
  (6)   ├─ Per-period conflict ────────────▶ Postgres  (Statements: CustomerId & Period & Active)
        │     gate  exists → return ActiveStatementExistsForPeriod (409)
        │           // business rule: at most one LIVE statement per (customer, period)
        │
  (7)   ├─ Malware scan ───────────────────▶ ClamAV  (fail-closed)
        │     gate  not clean → metrics.UploadRejected("malware"); return MalwareDetected
        │           // an unreachable scanner BLOCKS the upload — never waves it through
        │
  (8)   ├─ PDF-encrypt ────────────────────▶ IPdfProtector  (AES; SA ID = open password)
        │     gate  throws → return InvalidFileContent
        │           // opening the PDF also rejects a structurally broken file
        │
  (9)   ├─ Store ──────────────────────────▶ S3  (TransferUtility → multipart >16MB)
        │           // canonical name Statement_{period}.pdf; caller filename discarded
        │
 (10)   ├─ Statement.Create(…, documentId, contentHash)
        │     Statements.Add + DownloadAuditLog.Add ; SaveChangesAsync ─▶ Postgres
        │     ╔ alt  DbUpdateException  (a concurrent upload won a unique index) ═══════════╗
        │     ║   delete the just-stored S3 object          // don't orphan the file        ║
        │     ║   re-read committed state to resolve which guard fired:                     ║
        │     ║     • (Customer, Period) active exists     → ActiveStatementExistsForPeriod ║
        │     ║     • DocumentId match                     → Success(winnerId)              ║
        │     ║     • (Customer, Period, ContentHash) match → Success(winnerId)             ║
        │     ║     • otherwise                            → rethrow                        ║
        │     ╚═══════════════════════════════════════════════════════════════════════════╝
        │
 (11)   └─ metrics.StatementUploaded(); return Success(statement.Id)
```

### The three de-dup layers (pre-check + hard index)

| Guard | Unique index | Catches | Response |
|---|---|---|---|
| **DocumentId** | `(CustomerId, DocumentId)` | one source redelivering its document | idempotent `Success(existing)` |
| **ContentHash** | `(CustomerId, Period, ContentHash)` | same bytes, same period, **any channel** | idempotent `Success(existing)` |
| **Period** | `(CustomerId, Period)` `WHERE status<>2` | one **live** statement per period | `409 ActiveStatementExistsForPeriod` |

**Why the ordering:** cheap DB checks and the content fingerprint run **before** the expensive scan/encrypt/store,
so a duplicate short-circuits without wasted work. The content-hash check runs **before** the per-period conflict
check so a true byte-identical redelivery *replays* (200 with the existing id) instead of dead-ending on a 409.

**Boundary (by design):** none of these catch the *same statement re-rendered to different bytes* — no
purely-derived identity exists for that; it falls back to the `(Customer, Period)` rule.
