# Demo UI — Secure Statement Delivery

A **standalone, static demo console** for exercising the backend API end-to-end, visually.

> This is **not** part of the product. The backend is the core; this UI is a thin,
> framework-free client (plain HTML/CSS/JS, no build step) kept deliberately separate
> from the solution so it never becomes a dependency of the backend.

## What it demonstrates

A **real product experience** following the user's journey, not an API tester:

- **Landing / sign in** — sign in or register (journey start).
- **Customer portal** ("My statements") — the customer views a statement, creates a **secure share
  link**, or downloads a **combined** date-range PDF.
- **Admin console** — an admin **finds a customer**, **delivers a statement** (regular or resumable
  upload), **revokes** one, and reviews the **audit trail**.

Which experience you see is driven by the signed-in user's role (decoded from the JWT).

Every action clearly **surfaces the API it calls**: a floating badge flashes the endpoint + status,
an inline caption under each action names the endpoint, and the **`</> API activity`** developer
drawer (top-right) shows every request/response in full.

## Prerequisites

- The backend running (via `dotnet run --project aspire/SecureStatementDelivery.AppHost`,
  or `dotnet run --project src/Web.Api`).
- The HTTPS dev cert trusted, so the browser will talk to `https://localhost:5001`:
  ```
  dotnet dev-certs https --trust
  ```
- **CORS**: the API only allows the origins listed under `Cors:AllowedOrigins` in
  `src/Web.Api/appsettings.Development.json`. This repo already whitelists ports
  **3000, 8000, 8080, 5500**. Serve the UI on one of those (below), or add your origin there.

## Run it

Pick any static server. From the `demo-ui/` folder:

```bash
# Node (serves on http://localhost:3000)
npx serve -l 3000 .

# or Python (http://localhost:8000)
python -m http.server 8000

# or VS Code "Live Server" extension (http://localhost:5500)
```

Then open the printed URL. In the top bar, set **API base URL**:
- Standalone Web.Api: `https://localhost:5001` (default).
- Via Aspire: copy the `web-api` HTTPS endpoint from the Aspire dashboard (the port is dynamic).

## Suggested demo script

**Admin journey (deliver a statement):**
1. On the landing screen, click *Use admin demo login* → **Sign in** → you land in the **Admin console**.
2. **Find a customer** by email (`cust@test.com`) → their profile appears.
3. **Deliver a statement** — *Generate sample PDF* → **Upload & deliver** (or tick *resumable* to
   watch the chunked progress bar).
4. The customer's statement list refreshes; try **Revoke**, then open the **Audit log** tab to show
   every action was recorded (append-only).

**Customer journey (receive & share):**
5. **Sign out**, then *Use customer demo login* → **Sign in** → you land in **My statements**.
6. **View** a statement (downloads the protected PDF), or **Share link** → a modal shows a single-use
   link → **Open link** downloads it anonymously (the token is the credential).
7. Try **Download combined PDF** for a date range.

Register a brand-new customer any time from the **Register** tab on the landing screen.

## Endpoint coverage

Every browser-appropriate endpoint is wired in:

- Auth: `POST /auth/register`, `POST /auth/login`, `POST /auth/refresh`
- Admin: `GET /admin/customers` (lookup), `GET /admin/audit-logs`
- Upload: `POST /customers/{id}/statements`, plus **resumable (TUS)** — `POST/PATCH /statements/upload/resumable`, `GET …/{fileId}/progress` (SSE), `GET …/{fileId}/result`
- Statements: `GET /statements`, `GET /statements/{id}` (details), `GET /statements/{id}/content` (in-app download), `POST /statements/{id}/download-tokens`, `GET /statements/download?token=` (redeem), `GET /statements/consolidated`, `DELETE /statements/{id}` (revoke)

**Intentionally excluded:** `POST /statements/ingest` — a **machine-to-machine** endpoint using the
`client_credentials` grant. Demonstrating it would mean putting a client secret in the browser, the
exact anti-pattern this project avoids. It's exercised by the integration tests instead.

The resumable panel requires the API's CORS to **expose** the `Location` / `Upload-Offset` headers
(added in `Program.cs`), so a browser TUS client can read them — restart the API after pulling this.

## Notes

- The **sample PDF** is generated client-side as a real, parseable one-page PDF so the
  backend's PDF password-protection accepts it. You can also pick your own PDF.
- **Download link redeem** is a plain browser navigation (the token is the credential, no auth
  header), so it works cross-origin without CORS. The in-app and consolidated downloads use the
  Bearer token, so they are fetched as blobs and saved.
- Nothing here is production auth: it uses the dev ROPC login purely for demo convenience.
