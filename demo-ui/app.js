"use strict";

// ---------------------------------------------------------------------------
// SecureStatements — demo front end. A real-feeling product (sign in → do your
// task) built as a small vanilla-JS SPA. Each user action surfaces the API it
// calls (floating badge + collapsible developer panel). No framework/build.
// ---------------------------------------------------------------------------

const state = {
  token: null, refresh: null, role: "customer", user: {},
  customer: null,      // admin's currently-selected customer
  sampleFile: null,
  share: null,         // { token, url }
  revokeTarget: null,  // statement id pending revoke
};

const $ = (id) => document.getElementById(id);
const base = () => $("baseUrl").value.trim().replace(/\/+$/, "");
const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));
function authHeaders(extra = {}) { return state.token ? { Authorization: `Bearer ${state.token}`, ...extra } : { ...extra }; }

// --- API-call indicator (toast) + developer log ----------------------------
function apiToast(method, path) {
  const el = $("apiToast");
  el.className = "api-toast show";
  el.innerHTML = `<span class="m">${method}</span> <span class="p">${esc(path)}</span> <span class="s">…</span>`;
  return (status, ok) => {
    const s = el.querySelector(".s"); if (s) s.textContent = status;
    el.classList.toggle("ok", ok); el.classList.toggle("err", !ok);
    clearTimeout(el._t); el._t = setTimeout(() => el.classList.remove("show"), 2800);
  };
}
function log(method, path, status, ok, detail) {
  const el = document.createElement("div");
  el.className = "entry";
  el.innerHTML = `<span class="m">${method}</span> <span class="path">${esc(path)}</span> ` +
    `<span class="${ok ? "s-ok" : "s-err"}">→ ${status}</span>` + (detail ? `<pre>${esc(detail)}</pre>` : "");
  $("log").prepend(el);
}
function clearLog() { $("log").innerHTML = ""; }
function toggleDrawer() { $("devDrawer").classList.toggle("hidden"); }

async function call(method, path, { json, form } = {}) {
  const done = apiToast(method, path);
  const headers = authHeaders();
  let body;
  if (json !== undefined) { headers["Content-Type"] = "application/json"; body = JSON.stringify(json); }
  else if (form !== undefined) { body = form; }
  let res, text, data;
  try { res = await fetch(base() + path, { method, headers, body }); }
  catch (e) { done("ERR", false); log(method, path, "network error", false, String(e) + "\n(API running? CORS + dev cert set up?)"); return { ok: false, status: 0, data: null }; }
  text = await res.text();
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  done(res.status, res.ok);
  log(method, path, res.status, res.ok, summarize(data));
  return { ok: res.ok, status: res.status, data };
}
function summarize(d) { if (d == null) return ""; return (typeof d === "string" ? d : JSON.stringify(d, null, 2)).slice(0, 500); }
function problem(d, fb) { return (d && typeof d === "object" && (d.detail || d.title)) || fb; }
function msg(id, ok, html) { $(id).innerHTML = `<span class="${ok ? "ok" : "err"}">${ok ? "✓" : "✗"}</span> ${html}`; }

// --- Router ----------------------------------------------------------------
function render() {
  const signedIn = !!state.token;
  $("view-auth").classList.toggle("hidden", signedIn);
  $("view-customer").classList.toggle("hidden", !(signedIn && state.role !== "admin"));
  $("view-admin").classList.toggle("hidden", !(signedIn && state.role === "admin"));
  $("signout").classList.toggle("hidden", !signedIn);
  $("refreshBtn").classList.toggle("hidden", !signedIn);
  $("whoami").classList.toggle("hidden", !signedIn);
  if (signedIn) {
    setWhoami();
    if (state.role === "admin") { adminTab("customers"); }
    else { $("cust_greeting").textContent = `Hi ${state.user.name || "there"} — your statements`; loadMyStatements(); }
  }
}
function setWhoami() {
  $("whoami").innerHTML = `Signed in as <b>${esc(state.user.name || state.user.email || "user")}</b>` +
    `<span class="role ${state.role === "admin" ? "admin" : ""}">${state.role}</span>`;
}
// Dev affordance: rotate the access token via the dev-only ROPC refresh endpoint. In production
// this flow is client↔Keycloak (grant_type=refresh_token); the API never proxies refresh.
async function refreshSession() {
  if (!state.refresh) { apiToast("POST", "/auth/refresh")("no token", false); return; }
  const r = await call("POST", "/auth/refresh", { json: { refreshToken: state.refresh } });
  if (r.ok && r.data.accessToken) { applyToken(r.data); setWhoami(); }
}

// --- Auth ------------------------------------------------------------------
function authTab(which) {
  document.querySelectorAll("#view-auth .tab").forEach((t) => t.classList.toggle("active", t.dataset.tab === which));
  $("pane-signin").classList.toggle("hidden", which !== "signin");
  $("pane-register").classList.toggle("hidden", which !== "register");
}
function fillSampleCustomer() {
  $("reg_email").value = "cust@test.com"; $("reg_first").value = "Cust"; $("reg_last").value = "One";
  $("reg_pw").value = "P@ssw0rd1"; $("reg_said").value = "8001015009087";
}

async function login() {
  const r = await call("POST", "/auth/login", { json: { email: $("login_email").value.trim(), password: $("login_pw").value } });
  if (r.ok && r.data.accessToken) { applyToken(r.data); render(); }
  else msg("login_out", false, problem(r.data, `Sign in failed (${r.status}).`));
}
function applyToken(data) {
  state.token = data.accessToken;
  state.refresh = data.refreshToken || state.refresh;
  const p = decodeJwt(data.accessToken);
  const roles = (p.realm_access && p.realm_access.roles) || [];
  state.role = roles.includes("admin") ? "admin" : "customer";
  state.user = { email: p.email || p.preferred_username || "", name: p.name || p.given_name || (p.email || "").split("@")[0], sub: p.sub };
}
function decodeJwt(jwt) { try { return JSON.parse(atob(jwt.split(".")[1].replace(/-/g, "+").replace(/_/g, "/"))); } catch { return {}; } }
function logout() { state.token = null; state.refresh = null; state.customer = null; render(); }

async function registerCustomer() {
  const r = await call("POST", "/auth/register", { json: {
    email: $("reg_email").value.trim(), firstName: $("reg_first").value.trim(), lastName: $("reg_last").value.trim(),
    password: $("reg_pw").value, southAfricanIdNumber: $("reg_said").value.trim() } });
  if (r.ok) msg("reg_out", true, `Account created (id <span class="pill">${r.data.id}</span>). Switch to “Sign in”.`);
  else msg("reg_out", false, problem(r.data, `Registration failed (${r.status}).`));
}

// --- Customer portal -------------------------------------------------------
async function loadMyStatements() {
  const r = await call("GET", "/statements");
  if (!r.ok) { $("cust_list").innerHTML = `<p class="empty">Could not load statements (${r.status}).</p>`; return; }
  renderStatements($("cust_list"), (r.data && r.data.items) || [], "customer");
}
async function downloadConsolidated() {
  const from = $("con_from").value.trim(), to = $("con_to").value.trim();
  await downloadBlob(`/statements/consolidated?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, `statements-${from}_to_${to}.pdf`, "con_out");
}

// --- Admin console ---------------------------------------------------------
function adminTab(which) {
  document.querySelectorAll("#view-admin .tab").forEach((t) => t.classList.toggle("active", t.dataset.atab === which));
  $("atab-customers").classList.toggle("hidden", which !== "customers");
  $("atab-audit").classList.toggle("hidden", which !== "audit");
  if (which === "audit") loadAudit();
}
async function lookupCustomer() {
  const email = $("lookup_email").value.trim();
  const r = await call("GET", `/admin/customers?email=${encodeURIComponent(email)}`);
  if (!r.ok) { $("customer_detail").classList.add("hidden"); msg("lookup_out", false, problem(r.data, `No customer found (${r.status}).`)); return; }
  state.customer = r.data;
  msg("lookup_out", true, `Found <b>${esc(r.data.firstName)} ${esc(r.data.lastName)}</b>.`);
  const initials = ((r.data.firstName || "?")[0] + (r.data.lastName || "")[0]).toUpperCase();
  $("cust_avatar").textContent = initials;
  $("cust_name").textContent = `${r.data.firstName} ${r.data.lastName}`;
  $("cust_meta").innerHTML = `${esc(r.data.email)} · id <span class="pill">${r.data.id}</span> · ${r.data.isActive ? "active" : "inactive"}`;
  $("customer_detail").classList.remove("hidden");
  loadCustomerStatements();
}
async function loadCustomerStatements() {
  if (!state.customer) return;
  const r = await call("GET", `/statements?customerId=${encodeURIComponent(state.customer.id)}`);
  if (!r.ok) { $("admin_list").innerHTML = `<p class="empty">Could not load (${r.status}).</p>`; return; }
  renderStatements($("admin_list"), (r.data && r.data.items) || [], "admin");
}
async function loadAudit() {
  const r = await call("GET", "/admin/audit-logs?page=1&pageSize=50");
  if (!r.ok) { $("audit_out").innerHTML = `<p class="empty">Could not load (${r.status}).</p>`; return; }
  const items = (r.data && r.data.items) || [];
  if (!items.length) { $("audit_out").innerHTML = `<p class="empty">No audit entries yet.</p>`; return; }
  $("audit_out").innerHTML = `<table><thead><tr><th>When</th><th>Action</th><th>Actor</th><th>Statement</th><th>IP</th><th>Detail</th></tr></thead><tbody>` +
    items.map((a) => `<tr><td>${new Date(a.occurredAt).toLocaleString()}</td><td>${esc(a.action)}</td><td>${esc(a.userName || "")}</td>` +
      `<td>${a.statementId ? `<span class="pill">${String(a.statementId).slice(0, 8)}…</span>` : "—"}</td><td>${esc(a.ipAddress || "")}</td><td>${esc(a.additionalData || "")}</td></tr>`).join("") +
    `</tbody></table>`;
}

// --- Deliver a statement (regular or resumable) ----------------------------
// Files at or above this size are delivered via the resumable (TUS) endpoint so large / unreliable
// transfers can survive an interrupted connection. Smaller files use the single-shot multipart POST.
// Ticking "resumable" forces TUS regardless of size (handy for demoing the resumable path).
const RESUMABLE_THRESHOLD_BYTES = 10 * 1024 * 1024; // 10 MB

function makeSamplePdf() {
  state.sampleFile = buildSamplePdf(`Sample statement ${$("up_period").value || ""}`);
  msg("up_out", true, "Sample PDF ready — click “Upload &amp; deliver”.");
}
function makeLargeSamplePdf() {
  state.sampleFile = buildSamplePdf(`Large sample statement ${$("up_period").value || ""}`, 12 * 1024 * 1024);
  msg("up_out", true, "Large ~12 MB sample ready — “Upload &amp; deliver” will auto-route to the resumable (TUS) path.");
}
async function deliverStatement() {
  if (!state.customer) { msg("up_out", false, "Find a customer first."); return; }

  const picked = $("up_file").files[0];
  const file = picked || state.sampleFile;
  if (!file) { msg("up_out", false, "Choose a PDF or click “Generate sample PDF”."); return; }

  // Auto-branch on the locally-known file size; the checkbox is an explicit override.
  if ($("up_resumable").checked) return resumableDeliver();
  if (file.size >= RESUMABLE_THRESHOLD_BYTES) {
    msg("up_out", true, `File is ${(file.size / (1024 * 1024)).toFixed(1)} MB — using resumable (TUS) upload.`);
    return resumableDeliver();
  }

  const form = new FormData();
  form.append("file", file, picked ? picked.name : "sample-statement.pdf");
  form.append("Period", $("up_period").value.trim());
  form.append("Description", $("up_desc").value.trim());
  const r = await call("POST", `/customers/${state.customer.id}/statements`, { form });
  if (r.ok) { msg("up_out", true, `Delivered. Statement id <span class="pill">${r.data.id}</span>`); loadCustomerStatements(); }
  else msg("up_out", false, problem(r.data, `Upload failed (${r.status}).`));
}

// --- Statement cards -------------------------------------------------------
function renderStatements(container, items, mode) {
  if (!items.length) { container.innerHTML = `<p class="empty">No statements yet.</p>`; return; }
  container.innerHTML = items.map((s) => `
    <div class="scard">
      <div class="period">${esc(s.period)}</div>
      <div class="st ${s.status}">${s.status}</div>
      <div class="desc">${esc(s.description || "")}</div>
      <div class="meta">${(s.fileSizeBytes / 1024).toFixed(0)} KB · ${new Date(s.createdAt).toLocaleDateString()}${s.isPasswordProtected ? " · 🔒 protected" : ""}</div>
      <div class="acts">
        <button class="btn btn-primary sm" onclick="viewStatement('${s.id}')">View</button>
        <button class="btn btn-ghost sm" onclick="statementDetails('${s.id}')">Details</button>
        ${mode === "customer"
          ? `<button class="btn btn-ghost sm" onclick="shareStatement('${s.id}')">Share link</button>`
          : `<button class="btn btn-ghost sm" onclick="revoke('${s.id}')">Revoke</button>`}
      </div>
    </div>`).join("");
}
async function viewStatement(id) { await downloadBlob(`/statements/${id}/content`, "statement.pdf"); }

async function statementDetails(id) {
  const r = await call("GET", `/statements/${id}`);
  if (!r.ok) { notice(problem(r.data, `Could not load details (${r.status}).`), false); return; }
  renderKv("detail_body", r.data);
  $("detailModal").classList.remove("hidden");
}
// Render an object as a themed key → value list (shared by the details and share modals).
function renderKv(containerId, obj) {
  const label = (k) => k.replace(/([A-Z])/g, " $1").replace(/^./, (c) => c.toUpperCase()).trim();
  const fmt = (v) => (v === null || v === undefined || v === "" ? "—" : String(v));
  $(containerId).innerHTML = Object.entries(obj)
    .map(([k, v]) => `<div class="kv-row"><span class="kv-k">${esc(label(k))}</span><span class="kv-v">${esc(fmt(v))}</span></div>`)
    .join("");
}

function revoke(id) {
  state.revokeTarget = id;
  $("revoke_reason").value = "Superseded by a corrected statement";
  $("revoke_out").innerHTML = "";
  $("revokeModal").classList.remove("hidden");
}
async function confirmRevoke() {
  if (!state.revokeTarget) return;
  const r = await call("DELETE", `/statements/${state.revokeTarget}`, { json: { reason: $("revoke_reason").value.trim() } });
  if (r.status === 204) { closeModal(); notice("Statement revoked.", true); loadCustomerStatements(); }
  else msg("revoke_out", false, problem(r.data, `Revoke failed (${r.status}).`));
}

// --- Share-link modal ------------------------------------------------------
async function shareStatement(id) {
  const r = await call("POST", `/statements/${id}/download-tokens`, { json: { isSingleUse: true, expiryMinutes: 15 } });
  if (!r.ok) { notice(problem(r.data, `Could not create link (${r.status}).`), false); return; }
  state.share = { token: r.data.downloadToken, url: `${base()}/statements/download?token=${encodeURIComponent(r.data.downloadToken)}` };
  renderKv("share_body", {
    "Single use": r.data.isSingleUse ? "Yes" : "No",
    "Expires": new Date(r.data.expiresAt).toLocaleString(),
    "Token": r.data.downloadToken.slice(0, 28) + "…",
  });
  $("share_token").value = state.share.url;
  $("shareModal").classList.remove("hidden");
}
function openShareLink() {
  if (!state.share) return;
  apiToast("GET", "/statements/download?token=…")("opening", true);
  log("GET", "/statements/download?token=…", "new tab", true, "Anonymous redeem — token is the credential.");
  window.open(state.share.url, "_blank");
}
function copyShareLink() {
  if (state.share && navigator.clipboard) { navigator.clipboard.writeText(state.share.url); notice("Link copied to clipboard.", true); }
}
function closeModal() {
  ["shareModal", "detailModal", "revokeModal"].forEach((m) => $(m).classList.add("hidden"));
}

// Centered toast for non-API messages (errors, confirmations).
function notice(text, ok = true) {
  const el = $("notice");
  el.textContent = text;
  el.className = "notice show " + (ok ? "ok" : "err");
  clearTimeout(el._t);
  el._t = setTimeout(() => el.classList.remove("show"), 3200);
}

// --- Authenticated blob download -------------------------------------------
async function downloadBlob(path, fallbackName, outId) {
  const done = apiToast("GET", path);
  let res;
  try { res = await fetch(base() + path, { headers: authHeaders() }); }
  catch (e) { done("ERR", false); log("GET", path, "network error", false, String(e)); return; }
  if (!res.ok) { const t = await res.text(); done(res.status, false); log("GET", path, res.status, false, t.slice(0, 300)); if (outId) msg(outId, false, `Failed (${res.status}).`); return; }
  const blob = await res.blob();
  const cd = res.headers.get("Content-Disposition") || "";
  const m = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(cd);
  const name = m ? decodeURIComponent(m[1].replace(/"/g, "")) : fallbackName;
  triggerDownload(blob, name);
  done(res.status, true); log("GET", path, res.status, true, `Downloaded "${name}" (${(blob.size / 1024).toFixed(0)} KB)`);
  if (outId) msg(outId, true, `Downloaded “${name}”.`);
}
function triggerDownload(blob, name) {
  const url = URL.createObjectURL(blob); const a = document.createElement("a");
  a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
}

// --- Resumable (TUS) delivery ----------------------------------------------
async function resumableDeliver() {
  const period = $("up_period").value.trim();
  const picked = $("up_file").files[0];
  const source = picked || state.sampleFile || buildSamplePdf(`Resumable sample ${period}`, 2 * 1024 * 1024);
  const bytes = new Uint8Array(await source.arrayBuffer());

  $("up_progwrap").classList.remove("hidden"); setBar(0); $("up_sse").textContent = "";
  const meta = [`customerId ${b64(state.customer.id)}`, `period ${b64(period)}`, `contentType ${b64("application/pdf")}`, `description ${b64($("up_desc").value.trim())}`].join(",");

  const done = apiToast("POST", "/statements/upload/resumable");
  let createRes;
  try { createRes = await fetch(base() + "/statements/upload/resumable", { method: "POST", headers: authHeaders({ "Tus-Resumable": "1.0.0", "Upload-Length": String(bytes.length), "Upload-Metadata": meta }) }); }
  catch (e) { done("ERR", false); msg("up_out", false, "Network error creating upload."); return; }
  done(createRes.status, createRes.ok);
  log("POST", "/statements/upload/resumable (create)", createRes.status, createRes.ok, `Upload-Length=${bytes.length}`);
  if (!createRes.ok) { msg("up_out", false, `Create failed (${createRes.status}).`); return; }
  const loc = createRes.headers.get("Location");
  if (!loc) { msg("up_out", false, "Create OK but Location header not readable — restart the API for the CORS expose-headers change."); return; }
  const url = loc.startsWith("http") ? loc : base() + (loc.startsWith("/") ? loc : "/" + loc);
  const fileId = url.split("/").pop();
  consumeSse(fileId);

  const CHUNK = 256 * 1024; let offset = 0;
  while (offset < bytes.length) {
    const slice = bytes.subarray(offset, Math.min(offset + CHUNK, bytes.length));
    let pr;
    try { pr = await fetch(url, { method: "PATCH", headers: authHeaders({ "Tus-Resumable": "1.0.0", "Content-Type": "application/offset+octet-stream", "Upload-Offset": String(offset) }), body: slice }); }
    catch (e) { msg("up_out", false, "Chunk failed (network)."); return; }
    if (!pr.ok) { log("PATCH", "…/resumable/" + fileId, pr.status, false, await pr.text()); msg("up_out", false, `Chunk failed (${pr.status}).`); return; }
    const c = parseInt(pr.headers.get("Upload-Offset") || String(offset + slice.length), 10);
    offset = Number.isNaN(c) ? offset + slice.length : c;
    setBar(offset / bytes.length); await delay(120);
  }
  log("PATCH", "…/resumable/" + fileId, "complete", true, `${bytes.length} bytes`);
  msg("up_out", true, "Uploaded — finalising (scan, protect, store)…");
  for (let i = 0; i < 30; i++) {
    await delay(1000);
    const rr = await call("GET", `/statements/upload/resumable/${fileId}/result`);
    if (rr.status === 200) { msg("up_out", true, `Delivered. Statement id <span class="pill">${rr.data.statementId}</span>`); loadCustomerStatements(); return; }
    if (rr.status === 400) { msg("up_out", false, `Finalisation failed: ${rr.data && rr.data.error}`); return; }
  }
  msg("up_out", false, "Timed out waiting for finalisation.");
}
async function consumeSse(fileId) {
  try {
    const res = await fetch(base() + `/statements/upload/resumable/${fileId}/progress`, { headers: authHeaders({ Accept: "text/event-stream" }) });
    if (!res.ok || !res.body) return;
    const reader = res.body.getReader(); const dec = new TextDecoder(); let buf = "";
    for (;;) { const { value, done } = await reader.read(); if (done) break; buf += dec.decode(value, { stream: true });
      let i; while ((i = buf.indexOf("\n\n")) >= 0) { const b = buf.slice(0, i); buf = buf.slice(i + 2);
        const line = b.split("\n").find((l) => l.startsWith("data: ")); if (!line) continue;
        try { const d = JSON.parse(line.slice(6)); $("up_sse").textContent = d.status === "completed" ? "server progress (SSE): completed" : `server progress (SSE): ${d.percent}%`; } catch {} } }
  } catch {}
}

// --- Helpers ---------------------------------------------------------------
function setBar(f) { $("up_bar").style.width = `${Math.round(Math.max(0, Math.min(1, f)) * 100)}%`; }
function b64(s) { return btoa(unescape(encodeURIComponent(s))); }
function delay(ms) { return new Promise((r) => setTimeout(r, ms)); }

function buildSamplePdf(title, padBytes = 0) {
  const safe = String(title).replace(/[()\\]/g, "").replace(/[^\x20-\x7E]/g, "");
  const stream = `BT /F1 22 Tf 72 700 Td (${safe}) Tj ET`;
  const objs = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"];
  let pdf = "%PDF-1.4\n"; const off = [];
  objs.forEach((o, i) => { off.push(pdf.length); pdf += `${i + 1} 0 obj\n${o}\nendobj\n`; });
  const xref = pdf.length;
  pdf += `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n` + off.map((o) => String(o).padStart(10, "0") + " 00000 n \n").join("");
  pdf += `trailer\n<< /Size ${objs.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
  if (padBytes && pdf.length < padBytes) pdf += "\n%" + "0".repeat(padBytes - pdf.length - 2);
  return new Blob([new TextEncoder().encode(pdf)], { type: "application/pdf" });
}

// --- Boot ------------------------------------------------------------------
$("baseUrl").value = localStorage.getItem("ssd_base") || $("baseUrl").value;
$("baseUrl").addEventListener("change", () => localStorage.setItem("ssd_base", base()));
render();
