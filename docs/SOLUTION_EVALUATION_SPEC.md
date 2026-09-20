# Solution Evaluation & Build Specification (Principal-Grade)

**What this file is.** A self-contained, project-agnostic rubric + evaluator prompt reverse-engineered from a real Principal-level technical assessment (`PROJECT_ASSESSMENT_EPR-698.md`). It encodes the **exact scoring mechanism** that assessment used so that:

- an AI *builder* can construct a solution that scores 100% (1000/1000) and passes every listed case, and
- an AI *evaluator* can score any solution deterministically and reproducibly.

**How to use it.**

- **To build for 100%:** treat every "MUST" as a definition-of-done gate. Satisfy every category's *Exceptional (10) requirement*, every *conjunctive Score-5 item*, avoid every *red flag*, and clear every *gate floor*. Then run the **Self-Verification Loop** (§9) until it reports zero open items.
- **To evaluate a solution:** follow §2 (mechanism), §3–§4 (per-category scoring), §5–§6 (overrides), and emit the report in the exact shape of §8. Score the **artefact**, not the process.

**Design intent.** This is written to generalize to *any* production-grade software project (service, library, CLI, pipeline, frontend, data job, infra module). Category names are generalized; the fraud-engine specifics from the source assessment are preserved only as worked examples.

---

## 0. Reviewer / Builder Operating Principles (Principal Mindset)

1. **Evidence over assertion.** Every score, claim, or "done" MUST cite a concrete artefact location (`path:line`), a command + its output, or an explicit "not present — searched X". Never infer capability from README prose alone; verify in code, config, and tests.
2. **Correctness across the whole state space, not the happy path.** A feature is "done" only when it is correct under the failure modes the system itself makes possible (retries, redelivery, partial failure, concurrency, restart, missing config, malformed input, empty/huge inputs).
3. **The system's own guarantees are requirements, not caveats.** If the design chooses at-least-once delivery, eventual consistency, async processing, or a cache, then duplicate/out-of-order/stale/concurrent behavior is in-scope and MUST be handled — not deferred.
4. **Trace the actual failure mode.** Generic acknowledgment ("we should add idempotency") scores as boilerplate. Naming the specific failure (e.g. "insert after commit but before ack → PK violation → nack/requeue → infinite loop") scores as understanding.
5. **Proportional engineering.** Abstractions must be justified by the problem size. Over-building (a pluggable engine for 4 rules) is penalized the same way under-building is. Match complexity to the problem.
6. **Absolute rules don't scale with cleverness.** "No secrets in source," "no injection," "no PII in logs" are floors at *every* level; no amount of surrounding quality buys them back.
7. **Confidence is part of the score.** Where a judgment is unexecuted or a runtime property (e.g. container user, does-the-loop-actually-reproduce), mark confidence Medium/Low and state exactly what would resolve it.
8. **Error asymmetry drives the verdict.** State the cost of a false-pass vs a false-fail explicitly and let it break ties.

---

## 1. Deliverable & Level Model

| Field | Meaning | Builder action |
|---|---|---|
| **Target Level** | e.g. SE1 / SE2 / SE3 / Staff / Principal. Sets gate floors and the bar for "Exceptional". | Declare it. Higher levels raise floors (see §5) and expect more of the "9–10" anchors met unprompted. |
| **Role/Track** | Backend / Frontend / Data / Platform / Full-stack / Library. | Selects which conditional categories are *applicable* (§4). |
| **Brief** | The stated problem. | Extract every explicit requirement into the §4.1 coverage table; resolve ambiguities and *document* the resolution. |
| **Execution mode** | Static review vs executed. | If static-only, every behavioral claim drops to Medium confidence with a stated "what would resolve it." A 100% solution MUST be executable-verified end to end. |

---

## 2. The Scoring Mechanism (exact, deterministic)

This is the engine. It is not a vibe; it is arithmetic with three override rules.

### 2.1 Point formula

```
points_earned(category) = round((score_0_to_10 / 10) * max_points(category), 1)
final_score            = Σ points_earned  over all applicable categories
percentage             = final_score / Σ max_points(applicable) * 100
```

Linear. A score of 10 yields full max points. **100% → score 10 in every applicable category.**

### 2.2 Tiers → max points (must sum to 1000 when all 13 apply)

| Tier | Weight rationale | Max points |
|---|---|---|
| **Primary** | The category the brief most centrally tests (here: Security). | **120** |
| **Important** | Core engineering competence. | **85** (default) |
| **Important (scoped-smaller)** | Narrower-surface Important categories. | **70** (Requirements Coverage), **50** (Problem Interpretation) |
| **Supporting** | Real but secondary signal. | **55** |

> When a category is **not applicable** (scope excluded, marked `*`), remove its max from the denominator and redistribute nothing — percentage is computed over *applicable* max only. Excluded categories are marked `*` and never penalized.

### 2.3 The 0–10 score anchors (apply to every category)

| Score | Band | Meaning |
|---|---|---|
| 0 | Absent | Not attempted. |
| 1–2 | Deficient | Present but fundamentally broken: brittle, happy-path-only, "tests that always pass," misunderstands the concern. |
| 3–4 | Developing | Real attempt with a **verified red flag** OR a missing **conjunctive Score-5 item** capping it below 5. |
| 5 | Competent | **All** Score-5 conjunctive items met, correct on the primary path; no red flags. This is a *floor*, not a menu. |
| 6–7 | Strong | Exceeds Competent: verified beyond the primary path, clean and consistent, proportional. |
| 8 | Exceptional (held) | Meets the Score-9 anchor but with one small, named residual gap (e.g. no schema versioning, one lifetime inconsistency). |
| 9 | Exceptional | Every anchor item met; divergences from the ideal are deliberate and explained. |
| 10 | Exceptional (max) | 9 + no residual gaps at all; would pass a Principal review with nothing to add. **This is the 100% target.** |

### 2.4 The three OVERRIDE rules (these beat the anchors)

1. **Red flag = ceiling constraint.** A verified red flag (§6) caps the category **below 5** no matter how strong everything else is. Red flags are ceilings, not footnotes.
2. **Conjunctive Score-5 items = minimum set, not a menu.** Each category's Score-5 list is *AND*, not *OR*. Missing even one caps below 5 regardless of excellence elsewhere. (Example from source: Data Modelling was excellent throughout but missing a migration strategy → capped at 4.)
3. **Given-level gate failure = absolute floor breach.** Some expectations are floors at the target level *and every level below it* (§5). Breaching one is flagged independent of the numeric score and forces a Panel Attention Flag; at higher levels it can independently block "Advance."

### 2.5 Confidence

Each category carries **High / Medium / Low** confidence. Lower it when the judgment is a runtime property not executed, a weighting judgment call, or inference rather than direct evidence. For each Medium/Low: state *what is unresolved* and *what would resolve it* (§8.5). A 100% build eliminates Medium/Low by executing and evidencing.

### 2.6 Bands & verdict

Percentage band: **Deficient** <35 | **Developing** 35–49 | **Competent** 50–64 | **Strong** 65–84 | **Exceptional** 85–100

Verdict inputs: final% + band, gate failures, deficit shape (broad vs concentrated), confidence, and the stated error-asymmetry (cost of false-pass vs false-fail).

**A 100% solution:** Exceptional band, zero gate failures, zero red flags, all categories High confidence, verdict "Strong Advance / Hire" with no reservations.

---

## 3. Global Definition of Done for 100% (applies to every project)

A solution scores 10 everywhere only if **all** of the following hold. Treat as blocking gates.

- [ ] **Every stated requirement** implemented AND verified end-to-end by an executed test, not just present. Business rules/constants match the spec exactly.
- [ ] **Correct beyond the happy path:** the system's own guarantees (delivery semantics, consistency model, concurrency, restart, retries) are each handled and tested.
- [ ] **No secrets in source** — none, not even "harmless" fallback literals. Missing config **fails fast** with a clear error. Secrets come from env/secret-store only.
- [ ] **No injection surface:** all external-data access parameterized/escaped; no string-built queries/commands.
- [ ] **No PII / secrets in logs or errors;** no stack traces leaked to clients (global error handler + safe error schema).
- [ ] **Idempotency** on every operation the domain requires to be exactly-once, keyed on a stable identifier, tested under redelivery/replay.
- [ ] **Error handling on every external integration point** (DB, broker, cache, HTTP, filesystem), with retry+backoff for transient and fail-fast/dead-letter for permanent, distinguished by a **typed error hierarchy**.
- [ ] **Input validation** at every trust boundary, returning a consistent, structured, field-level error schema.
- [ ] **Schema/versioning strategy** present (DB migrations, event schema versions, API versioning) — never "create-if-not-exists" as the production path.
- [ ] **Indexes match query shapes;** no N+1; collection endpoints paginated.
- [ ] **Both unit and integration tests** present and passing, covering boundaries, failure paths, and every I/O seam (API, persistence, messaging, external calls). Not happy-path-only.
- [ ] **Structured logging with correlation IDs** at every key point on **every** path (ingestion *and* processing), correct levels, no blind boundaries.
- [ ] **Graceful startup & shutdown:** readiness/health checks, dependency ordering, clean resource teardown, connection reuse (no per-call connection churn).
- [ ] **Containers/infra hardened:** multi-stage builds, minimal/pinned base images, explicit non-root `USER`, healthchecks on **all** dependencies with correct readiness gating, externalized config.
- [ ] **Docs carry reasoning:** architecture + diagram, each non-obvious decision justified, build/run/test instructions, API examples, and — critically — known trade-offs reasoned through *specifically* (the actual failure mode), not listed generically.
- [ ] **Internally consistent:** one style, one set of patterns, consistent resource lifetimes, no commented-out code, no dead code.
- [ ] **Executed:** it builds, boots, and the documented scenarios reproduce. Every behavioral claim has been run.

---

## 4. Category Rubrics (score each 0–10)

Notation per category: **Tier / Max pts**, then: *Conjunctive Score-5 floor* (all required to reach 5), *Red flags* (cap below 5), *Gate floor* (absolute), *Exceptional-10 requirement* (the 100% bar), *Evidence to collect*, and a *Generalized checklist*.

> `*` = **conditional category**: score only if in scope for the role/architecture; otherwise mark `*-Excluded` and drop from the denominator. Never penalize an excluded category.

### 4.1 Requirements Coverage — Important (scoped-smaller) — **70 pts**

- **Below 5 if:** any stated requirement missing/non-functional, or a rule constant wrong.
- **6–8:** correctness verified *beyond* the primary path (the full state space the design implies).
- **Exceptional-10:** all requirements delivered, correct across the entire implied state space (delivery/consistency/concurrency), every rule/constant test-verified against the spec.
- **Evidence:** a requirement → status → `path:line` → test table (see below). **Builder MUST produce this table.**

| Requirement (verbatim from brief) | Status | Impl evidence | Test evidence |
|---|---|---|---|
| … | Implemented / Partial / Missing | `path:line` | `path:line` (passing) |

### 4.2 Problem Interpretation — Important (scoped-smaller) — **50 pts**

- **Score-5 floor (AND):** ambiguities identified; resolutions documented; scope correctly bounded; proportional to the brief.
- **Exceptional-10:** every ambiguity surfaced and resolved with stated reasoning; scope neither gold-plated nor under-cut; the system's implied guarantees named explicitly.
- **Evidence:** the assumptions/decisions section; any scope call-outs.

### 4.3 Architecture — Important — **85 pts**

- **Score-5 floor (AND):** clear separation of concerns; data flow traceable; no hot-path pathologies on the primary path.
- **Red flags (cap <5):** exactly-once domain op with no idempotency key/state guard; unbounded in-memory growth; hot-path N+1/full scan.
- **Exceptional-10:** proportional design; guarantees (delivery/consistency/concurrency/restart) handled by design; bounded resources; no hot-path pathologies anywhere; abstractions justified by problem size.
- **Evidence:** component/data-flow trace; the idempotency/state-guard mechanism; resource-bounding proof.

### 4.4 Resilience & Error Handling — Important — **85 pts**

- **Score-5 floor (AND):** error handling present on the primary integration points; transient vs permanent distinguished somewhere.
- **Red flags (cap <5):** any external integration point with zero error handling; one generic catch treating transient == poison; no DLQ/max-retry on a replayable transport.
- **Exceptional-10:** typed error hierarchy; retry+backoff for transient, DLQ+max-retry for permanent; every external integration point covered; failure paths tested.
- **Evidence:** each external call site; the error taxonomy; retry/DLQ config; failure-path tests.

### 4.5 Data Modelling — Important — **85 pts**

- **Score-5 floor (AND):** correct types/constraints/keys; normalized appropriately; **migration strategy present**; indexes exist.
- **Red flags / conjunctive misses (cap <5):** any Score-5 item absent — *especially no migration strategy* — caps below 5.
- **Exceptional-10:** all above + indexes match query shapes, no N+1, versioned migrations (never create-if-not-exists in prod), constraints enforce every invariant.
- **Evidence:** schema/DDL; migration files; index-to-query-shape mapping.

### 4.6 API Design `*` — Important — **85 pts** *(applicable when an API surface exists)*

- **Score-5 floor (AND):** correct HTTP/protocol semantics; **input validation present**; **structured error schema present**.
- **Red flags / conjunctive misses (cap <5):** no input validation, or no error schema, caps below 5.
- **Exceptional-10:** consistent field-level validation + error schema; correct status codes; pagination on collections; versioning; idempotent where semantics require.
- **Evidence:** validation at every boundary; the error-schema definition; pagination/versioning.

### 4.7 Security — Primary — **120 pts**

- **Score-5 floor (AND):** parameterized data access (no injection); no PII/secrets in logs; no stack-trace leakage; authn/authz present where the brief implies an external caller (no IDOR).
- **GATE FLOOR (absolute, every level):** **no secrets in application source** — not even fallback literals. Breach = gate failure + Panel Attention Flag, independent of the numeric score. (Config-file/env credentials are fine; *source-code* literals are the failure.)
- **Red flags (cap <5):** any injection surface; PII in logs; missing authz on an externally-callable resource (IDOR).
- **Exceptional-10:** fail-fast on missing config; secrets from a store/env only; least-privilege everywhere; authn+authz with resource ownership checks; safe errors; dependency/vuln hygiene; no injection anywhere.
- **Evidence:** grep for credential literals across all source; every data-access call; every log statement (scan for PII); env-vs-source split for every secret.

### 4.8 Code Quality & Maintainability — Important — **85 pts**

- **Score-5 floor (AND):** clean layering; thin handlers with logic separated; consistent patterns; proportional abstraction.
- **Exceptional-10:** all above + *fully internally consistent* (uniform resource lifetimes — e.g. no singleton that opens/closes a connection per call), no dead/commented-out code, naming and structure a reviewer would not touch.
- **Evidence:** layer boundaries; a scan for lifetime/style inconsistencies; commented-out/dead code scan.

### 4.9 Testing — Important — **85 pts**

- **Score-5 floor (AND):** **both** unit *and* integration tests present, well-structured (AAA, descriptive, independent), covering boundaries.
- **Red flag (cap <5, here capped at 3):** no integration tests on a non-trivial system (multi-service / broker / DB) — unit-only is a red flag regardless of unit-test quality. Also: tests that can't fail / happy-path-only.
- **Exceptional-10:** unit + integration + boundary + failure-path + concurrency/redelivery tests covering **every** I/O seam (API, persistence, messaging, external calls); exact boundary values and negative/out-of-order cases tested.
- **Evidence:** the full test file listing; which seams are and are not covered; boundary/negative cases present.

### 4.10 Observability — Supporting — **55 pts**

- **Score-5 floor (AND):** meaningful logs at key points; correct levels; no PII.
- **Red flag (cap <5):** a whole path/boundary with **no logging** (blind on failure) — e.g. an instrumented worker but a silent ingestion API.
- **Exceptional-10:** structured logs with correlation IDs on **every** path, metrics + traces, error logs carry the correlating id, no blind boundaries, dashboards/alerts describable.
- **Evidence:** log sites per path; correlation-id presence in both success and error logs; the silent-path check.

### 4.11 Event-Driven Design `*` — Important — **85 pts** *(applicable when messaging/eventing is used)*

- **Score-5 floor (AND):** events named as past-tense facts (not commands); payload self-contained; no consumer-specific coupling.
- **Exceptional-10:** all above + a **documented event schema + versioning contract**, consumer-side idempotency resolved (not just acknowledged), no mutation of published events, would serve a second consumer unchanged.
- **Note:** held at 8 in the source because schema versioning was absent and consumer idempotency only generically acknowledged — both are required for 9–10.
- **Evidence:** event definition (naming, payload); schema/version doc; the consumer idempotency mechanism.

### 4.12 Infrastructure & DevOps — Supporting — **55 pts**

- **Score-5 floor (AND):** reproducible build (multi-stage where relevant); externalized config; dependency orchestration.
- **Exceptional-10:** all above + right-sized/pinned base images, **explicit non-root `USER`**, healthchecks on **all** dependencies with correct readiness gating (not merely "container started"), CI/CD describable, no config baked into images.
- **Evidence:** Dockerfile stages/base/USER; compose/manifest healthchecks + `depends_on` conditions per dependency.

### 4.13 Documentation & Communication — Supporting — **55 pts**

- **Score-5 floor (AND):** architecture evident; build/run/test instructions; decisions documented.
- **Exceptional-10:** all above + architecture diagram, each decision justified with reasoning, API examples, and known trade-offs **reasoned through specifically** (the actual failure mode named), not listed as generic boilerplate; no runbook-padding crowding out design content.
- **Evidence:** design-notes section; whether deferred-gaps are specific vs generic; diagram present.

---

## 5. Gate-Floor Master List (absolute, breach = flag independent of score)

These fail at the target level *and every level below it*. At higher target levels they can independently block "Advance."

| # | Gate | Passes when | Fails when (flag) |
|---|---|---|---|
| G1 | **No secrets in source** | All credentials/keys from env/secret-store; missing config fails fast. | Any credential/key literal in application source — *including* fallback defaults. |
| G2 | **No injection** | All external-data access parameterized. | Any string-built query/command with external input. |
| G3 | **No PII/secret leakage** | No PII/secrets in logs or client errors; no stack traces to clients. | Any PII/secret in a log or error response. |
| G4 | **Authz on external resources** | Resource access checks ownership/role where an external caller is implied. | Externally-callable resource with no access control (IDOR) *when the brief implies external callers*. |
| G5 | **Requirements floor** | All stated requirements functional. | Any stated requirement missing/non-functional. |
| G6 | **Exactly-once integrity** | Idempotency guard on operations the domain requires be exactly-once. | Such an operation with no guard, under an at-least-once/replayable transport. |

> Requirements Coverage scoring **below 5** and any Primary/Important category at **Low confidence** also trigger Panel Attention Flags (see §8.4), even without a gate breach.

---

## 6. Red-Flag Master List (verified red flag = ceiling below 5 for its category)

A red flag must be **verified with evidence** (traced code path, grep result, executed reproduction) — not inferred. Each caps its category below 5.

| Category | Red flag |
|---|---|
| Architecture | Exactly-once domain op with no idempotency key/state guard; unbounded in-memory growth; hot-path N+1/full scan. |
| Resilience | Any external integration point with zero error handling; one generic catch treating transient == poison; no DLQ/max-retry. |
| Data Modelling | *(conjunctive miss, not "red flag")* — any Score-5 item absent (esp. no migration strategy) caps below 5. |
| API Design | *(conjunctive miss)* — no input validation or no error schema caps below 5. |
| Security | Injection surface; PII in logs; IDOR; **plus** the G1 source-secrets gate. |
| Testing | No integration tests on a non-trivial system; tests that can't fail; happy-path-only. |
| Observability | A whole path/boundary with no logging (blind on failure). |

---

## 7. Worked Reference (from the source assessment calibration examples)

These are *examples of the mechanism*, not requirements to copy. They calibrate what each rule looks like in practice.

- **Gate breach (G1):** `Password=postgres` and `?? "guest"` fallback literals in `Program.cs` / `Worker.cs` → Security flagged, capped at 4 despite parameterized access + no PII in logs.
- **Red flag → ceiling (Architecture/Resilience 4):** unconditional insert keyed on `TransactionId` + nack/requeue-on-any-exception → PK violation → infinite redelivery loop. Traced, not inferred.
- **Conjunctive miss (Data 4, API 4):** excellent modelling but no migration strategy → capped at 4; correct HTTP semantics but no input validation and no error schema → capped at 4.
- **Held at 8 (Event-Driven, Docs):** everything for a 9 present *except* one named residual — no event schema versioning; deferred gaps stated generically rather than reasoned through.
- **Strong (Code Quality 7):** clean and consistent, but a singleton publisher opening/closing a connection per call → not "fully internally consistent" → held below 9.
- **Confidence Medium:** container non-root status not executable-verified (static review) → Infrastructure confidence Medium with "build image + `docker inspect`/`whoami`" as the resolver.

**To turn each of these into a 10:** fail-fast on missing config (no literals); upsert/unique-violation guard + DLQ + max-redelivery; add migrations; add input validation + error schema; add event schema versioning + reasoned docs; reuse a long-lived connection; execute and evidence the container user.

---

## 8. Evaluator Output Format (emit exactly this shape)

The evaluating AI MUST produce a report structured like the source assessment:

### 8.1 Header

Date, repository, ticket/brief, role, **target level**, execution mode (executed vs static-only).

### 8.2 Triage Decision

```
Verdict:      Advance / Do-Not-Advance / Strong-Advance
Final Score:  N / 1000 pts — NN.N%
Band:         <band>
Flags:        <count> raised — <categories>
Excluded (*): <categories or None>
```

Then 4–6 short paragraphs: **deficit shape** (broad vs concentrated, with the two largest losses quantified vs total shortfall), **gate failures**, **confidence**, **interview/decision value**, and a **weighed conclusion** invoking the explicit **error asymmetry** (cost of false-pass vs false-fail).

### 8.3 Score Breakdown table

`# | Category | Tier | Score | Label | Conf | Points Earned | Max Pts`, then Total and Final%. Recompute `points = score/10 × max`. Verify the sum.

### 8.4 Panel Attention Flags

State every trigger evaluated and whether it fired: category ≤2; given-level gate failure; Requirements Coverage < 5; Low-confidence Primary/Important. For each fired flag: **Observed** (with `path:line`), **Risk** (why it matters at this level), **Discussion/Remediation Point**.

### 8.5 Assessment Uncertainty table

`Category | Score | Confidence | What is unresolved | What would resolve it` — one row per Medium/Low.

### 8.6 Strengths / Key Concerns

3–5 each, every one anchored to `path:line` or an executed result.

### 8.7 Per-Category Assessments

For each: `Score/10 (Label) — pts/max — Tier — Confidence`, bullet evidence, red-flag call-out if any, and an explicit **Score rationale** naming which anchor/override set the ceiling or floor.

### 8.8 Improvement Opportunities

For each gap: **What was implemented**, **Stronger approach**, **Why it matters** — ordered by point impact.

### 8.9 Interview / Verification Questions (optional)

Probing (gaps), Verification (confirm depth in 7–10 categories), Calibration (level) — each with Strong/Adequate/Weak answer anchors.

### 8.10 Assessor Notes

Execution mode caveats, `*` scope decisions, Medium/Low-confidence list, any structural context (recorded, not scored).

---

## 9. Self-Verification Loop (for the BUILDER targeting 100%)

Run this to convergence before claiming done. Do not stop while any item is open.

1. **Requirements sweep.** Build the §4.1 table. Every row `Status=Implemented` AND has a passing test. If not → fix, repeat.
2. **State-space sweep.** For each design guarantee (delivery/consistency/concurrency/restart), write the failure it permits, then the guard, then a test that reproduces-then-passes. Idempotency, redelivery, out-of-order, malformed, empty, huge.
3. **Gate sweep (§5).** Grep all source for credential/key literals → zero. Confirm fail-fast on missing config. All data access parameterized. No PII in any log/error. Authz where external callers implied. Exactly-once guards present.
4. **Red-flag sweep (§6).** For each category's red flags, prove absence with evidence (grep/trace/test).
5. **Conjunctive-floor sweep.** For every category, confirm *each* Score-5 item is met (they are AND). Migrations, input validation, error schema, both unit+integration tests, etc.
6. **Exceptional sweep.** For every category, confirm the Score-10 requirement (§4). Where one residual remains, you're at 8 not 10 — close it.
7. **Execute.** Build, boot, run every documented scenario. Every behavioral claim → an actual run + captured output. Resolve every Medium/Low confidence to High.
8. **Consistency & docs.** One style, uniform resource lifetimes, no dead/commented code. Docs carry reasoning and name actual failure modes, not generic gaps. Architecture diagram present.
9. **Grade yourself.** Run §8 on your own artefact. If final < 1000/1000, any flag fired, any red flag, or any Medium/Low confidence remains → return to step 1 for those categories.

**Done =** 1000/1000, Exceptional band, zero flags, zero red flags, all High confidence, executed.

---

## 10. Quick Reference — the 100% checklist (print this)

- [ ] Every stated requirement: implemented + integration-tested + constants match spec
- [ ] Correct under the design's own guarantees (delivery/consistency/concurrency/restart)
- [ ] Idempotency on all exactly-once ops, tested under redelivery/replay
- [ ] Zero secrets in source; fail-fast on missing config; secrets from env/store
- [ ] Zero injection; all external-data access parameterized
- [ ] Zero PII/secrets in logs/errors; global error handler; safe error schema
- [ ] Error handling on EVERY external integration point (produce + consume)
- [ ] Typed error hierarchy: transient → retry+backoff, permanent → DLQ+max-retry
- [ ] Input validation at every boundary; consistent field-level error schema
- [ ] Migrations / schema + event + API versioning (never create-if-not-exists in prod)
- [ ] Indexes match query shapes; no N+1; collections paginated
- [ ] Unit + integration tests on EVERY seam; boundary + negative + concurrency cases
- [ ] Structured logs + correlation IDs on EVERY path; correct levels; no blind boundary
- [ ] Readiness/health checks; graceful shutdown; connection reuse (no per-call churn)
- [ ] Multi-stage builds; pinned minimal base; explicit non-root USER; healthchecks on ALL deps
- [ ] Externalized config; nothing secret baked into images
- [ ] Docs: diagram + reasoned decisions + build/run/test + API examples + specific trade-offs
- [ ] Internally consistent; no dead/commented code
- [ ] EXECUTED: builds, boots, every scenario reproduced, every claim run
- [ ] Self-graded via §8 == 1000/1000, zero flags, zero red flags, all High confidence
