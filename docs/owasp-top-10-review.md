# OWASP Top 10:2025 review — `/api`

A pass over `TaskTracker.Api` against the [OWASP Top 10:2025](https://owasp.org/Top10/2025/),
based on a full read of `Program.cs`, `Controllers/TasksController.cs`,
`Dtos/`, `Models/`, `Data/TaskDbContext.cs`, `appsettings*.json`,
`Properties/launchSettings.json`, and a live `dotnet list package
--vulnerable` scan of the project's dependency tree. Context worth
keeping in mind throughout: this is a single-tenant personal task
tracker with no concept of user accounts, run locally against a
developer's own SQL Server instance — several findings below would
matter far more (or differently) if this were deployed as a multi-user,
internet-facing service.

The 2025 list reshuffles the 2021 one in a few ways worth knowing up
front: **Broken Access Control** moves to #1, **Security
Misconfiguration** to #2, a new **Software Supply Chain Failures**
category (#3) absorbs what used to be "Vulnerable and Outdated
Components" plus broader build/dependency-pipeline concerns, and
**Server-Side Request Forgery** is retired as its own category — SSRF
is now treated as an instance of Broken Access Control (unauthorized
access to internal resources), so it's folded into the A01 write-up
below rather than getting its own section. A new **Mishandling of
Exceptional Conditions** category (#10) replaces it, covering error
handling rather than outbound-request abuse.

## Summary

| # | Category | Status |
|---|---|---|
| A01 | Broken Access Control (incl. SSRF) | ❌ Not covered |
| A02 | Security Misconfiguration | ✅ Covered |
| A03 | Software Supply Chain Failures | ✅ Covered |
| A04 | Cryptographic Failures | ✅ Covered |
| A05 | Injection | ✅ Covered |
| A06 | Insecure Design | ⚠️ Partially covered |
| A07 | Authentication Failures | ❌ Not covered |
| A08 | Software or Data Integrity Failures | ✅ Covered |
| A09 | Security Logging and Alerting Failures | ❌ Not covered |
| A10 | Mishandling of Exceptional Conditions | ⚠️ Partially covered |

---

## A01:2025 — Broken Access Control (including SSRF)

**Status: ❌ Not covered.**

There is no access control of any kind on any endpoint. `Program.cs`
calls `app.UseAuthorization()` (line 39), but that middleware has
nothing to enforce: no `[Authorize]` attribute appears anywhere in
`TasksController.cs`, and no authentication scheme is registered at all
(see A07) — so `UseAuthorization()` is effectively a no-op here.

Every action — `GetAll`, `GetById`, `Create`, `Update`, `Delete` — is
reachable by anyone who can send an HTTP request to the API, with no
notion of "whose task is this." `TaskItem` (`Models/TaskItem.cs`) has
no owning-user column, so even if authentication were added later,
today's data model has no way to scope a query to "tasks belonging to
the caller" — that's a schema change, not just a middleware change.

For a single-user local tool this may be an accepted, deliberate
trade-off rather than an oversight. It's flagged here because the
question was "is this covered," and structurally, it is not — anyone
who can reach `http://localhost:5189` can read, create, modify, or
delete every task.

**SSRF, folded into this category for 2025:** the API never makes
outbound requests derived from user-supplied input — no `HttpClient`
calls anywhere in `TasksController` or elsewhere in `TaskTracker.Api`,
no webhook/callback-URL fields on any DTO, no fetch-a-resource-by-URL
feature. There is currently no code path through which a request body
or query parameter could cause the server to issue an HTTP call to an
attacker-chosen destination — that sub-concern has no attack surface to
evaluate yet. Worth re-checking the moment any feature adds outbound
HTTP calls driven by client input (e.g., fetching a URL preview for a
task description).

**If this ever needs to be covered:** add an authentication scheme,
put `[Authorize]` on `TasksController`, add a `UserId` column to
`TaskItem` via a migration, and filter every query in
`TasksController` by the authenticated caller's ID — reads and writes
both, not just writes.

---

## A02:2025 — Security Misconfiguration

**Status: ✅ Covered.**

Handled well:

- Swagger UI and the OpenAPI document are only mapped inside
  `if (app.Environment.IsDevelopment())` (`Program.cs:28-33`) — they
  don't exist as routes in a production build.
- CORS (`Program.cs:17-23`) allow-lists exact origins
  (`http://localhost:4200`, `https://localhost:4200`) rather than using
  `AllowAnyOrigin()`, and never calls `AllowCredentials()` — so even
  the broad `.AllowAnyHeader().AllowAnyMethod()` on that policy doesn't
  combine with credentialed cross-origin requests, which is the
  genuinely dangerous combination.
- `appsettings.json` sets `"AllowedHosts": "localhost"` — scoped down
  from the stock `"*"` template value. The app only ever runs on
  `localhost` (`Properties/launchSettings.json`), so this rejects any
  request carrying an unexpected `Host` header at no cost to
  functionality.
- Security-response-header middleware (`Program.cs`, right after
  `app.UseHttpsRedirection()`) adds `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, and `Referrer-Policy: no-referrer` to every
  response, verified live via `curl -i` and pinned by
  `GetAll_ResponseIncludesSecurityHeaders` in
  `TasksControllerTests.cs`. `Content-Security-Policy` is deliberately
  omitted — this is a pure JSON API with no HTML views or
  user-controlled static content, so a browser-document-rendering
  control like CSP has no meaningful target here; worth adding only if
  the API ever serves HTML directly.
- `app.UseHttpsRedirection()` is present, but there's no `app.UseHsts()`
  for non-Development environments — see A04, where this is discussed
  alongside the rest of the app's transport-security posture. (Kept as
  an A04 finding rather than reopening this section, since it's
  specifically a transport-security gap.)

(General exception-handling configuration — what happens when an
unhandled error occurs — is discussed under A10 rather than here, since
2025 gave it a dedicated category.)

---

## A03:2025 — Software Supply Chain Failures

**Status: ✅ Covered.**

The concrete finding is fixed: `Microsoft.OpenApi` 2.0.0 was a
**transitive** dependency (pulled in via `Microsoft.AspNetCore.OpenApi`
10.0.9 in `TaskTracker.Api.csproj`) carrying a publicly disclosed
**high-severity** advisory
([GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)
/ CVE-2026-49451 — uncontrolled recursion parsing an OpenAPI document
with a circular schema reference can crash the process via stack
overflow). Both `TaskTracker.Api.csproj` and
`TaskTracker.Api.Tests.csproj` now pin a direct `PackageReference` to
`Microsoft.OpenApi` `2.7.6` (fixed at `2.7.5`+), overriding the
vulnerable transitive resolution. Verified clean:

```
$ dotnet list TaskTracker.slnx package --vulnerable --include-transitive

The given project `TaskTracker.Api` has no vulnerable packages given the current sources.
The given project `TaskTracker.Api.Tests` has no vulnerable packages given the current sources.
```

The surrounding supply-chain posture is also strengthened, not just the
one CVE:

- `RestorePackagesWithLockFile` is now enabled in both projects, and
  the generated `packages.lock.json` files are committed — builds pin
  to exact resolved versions (direct and transitive), so two builds a
  week apart can't silently drift onto a different, possibly
  vulnerable, dependency graph. Any future version bump shows up as a
  reviewable diff in the lock file.

Still absent, and out of scope for this fix:

- No Dependabot config or equivalent (`.github/` doesn't exist in this
  repo at all), so nothing automatically flags the *next* advisory as
  it's published.
- No CI pipeline exists yet (no `.github/workflows`), so there's no
  automated `dotnet list package --vulnerable` gate on pull requests —
  today, catching a new CVE still requires someone to run the scan by
  hand, the way this one was found. Worth revisiting once a CI
  pipeline exists at all for this repo.

---

## A04:2025 — Cryptographic Failures

**Status: ✅ Covered.**

Handled correctly:

- `app.UseHttpsRedirection()` (`Program.cs:35`) forces browser traffic
  onto TLS.
- The real connection string never reaches source control —
  `appsettings.Development.json` is gitignored (`.gitignore`), and
  `appsettings.json` (committed) has no `ConnectionStrings` section at
  all, per the convention `api/CLAUDE.md` documents.
- Local dev auth to SQL Server uses Windows Integrated Auth
  (`Trusted_Connection=True`) — no password travels over the wire at
  all for the default setup.
- `Program.cs` now adds `else { app.UseHsts(); }` alongside the existing
  `if (app.Environment.IsDevelopment())` block, matching the standard
  ASP.NET Core template. Verified two ways: `HstsTests.cs` confirms
  `Strict-Transport-Security` is present on an HTTPS response under a
  `Production`-environment test host and absent under `Development`;
  and manually via `ASPNETCORE_ENVIRONMENT=Production dotnet run
  --launch-profile https` + `curl -k -i https://localhost:7062/api/tasks`.
- `appsettings.Development.json`'s `TrustServerCertificate=True` and
  `appsettings.json`'s `AllowedHosts: "localhost"` (see A02) are both
  documented in `api/CLAUDE.md` as dev-only settings that must be
  updated together for a real deployment — not fixed in code here,
  since there's nothing to fix in a setting that's correct for its
  current gitignored/dev-only scope.

**A subtlety worth recording, found while verifying this fix:** ASP.NET
Core's `HstsMiddleware` never adds the `Strict-Transport-Security`
header for `localhost`/loopback requests, by design (so local
development is never HSTS-pinned in a browser) — confirmed directly:
`curl -k -i https://localhost:7062/api/tasks` in `Production` mode
returns `200` with no HSTS header, even with `app.UseHsts()` wired up.
Combined with `AllowedHosts: "localhost"` from A02 — which rejects any
non-`localhost` `Host` header with `400` — this means HSTS cannot
actually activate against this app *as currently deployed* (dev-only,
`localhost`-scoped). The fix is still correct and necessary: it's the
standard idiom, it's dormant only because every other setting in this
app is also currently scoped to `localhost`, and it activates
automatically the moment `AllowedHosts` is updated to a real hostname
for an actual deployment — which is exactly the scenario `api/CLAUDE.md`
now flags as something to update together.

---

## A05:2025 — Injection

**Status: ✅ Covered.**

Every database access in `TasksController.cs` goes through EF Core's
LINQ surface (`db.Tasks.Where(...)`, `FindAsync(...)`,
`FirstOrDefaultAsync(...)`) — there is no `FromSqlRaw`,
`ExecuteSqlRaw`, or hand-built SQL string anywhere in the codebase. EF
Core parameterizes every value it sends to SQL Server automatically
(see `docs/api-efcore-deep-dive.md` §5), which structurally rules out
classic SQL injection — there's no string concatenation path for user
input to reach a query as anything other than a parameter value.

The only other injection-adjacent surface is model binding
(`CreateTaskDto`, `UpdateTaskDto`), which goes through the standard
`System.Text.Json` deserializer — no custom deserialization, no
reflection-based binding beyond what `[ApiController]` does by default.
There's no shell execution, file-path construction from user input, or
XML processing anywhere in the API.

---

## A06:2025 — Insecure Design

**Status: ⚠️ Partially covered.**

One deliberate, effective design choice: controllers only ever bind
request bodies to DTOs (`CreateTaskDto`, `UpdateTaskDto`), never to the
`TaskItem` entity directly. That rules out the classic mass-assignment
class of bug — a client cannot set `Id` or `CreatedAt` through the API
no matter what JSON they send, because those DTOs simply don't expose
those fields (`Dtos/CreateTaskDto.cs`, `Dtos/UpdateTaskDto.cs`). This is
called out as intentional in `api/CLAUDE.md` ("Controllers return
DTOs, never EF entities directly... avoids leaking tracking state /
over-posting").

What's missing at the design level:

- No rate limiting or throttling on any endpoint — nothing stops a
  client from hammering `POST /api/tasks` in a loop. Low real-world
  risk for a local single-user tool, but it's an absent control, not a
  present-and-adequate one.
- The access-control gap from A01 is really a design-level gap, not a
  missing middleware line: the data model itself has no concept of
  ownership, so "insecure design" and "broken access control" are the
  same root cause here, viewed from two angles.

---

## A07:2025 — Authentication Failures

**Status: ❌ Not covered.**

There is no authentication mechanism in this codebase at all — no
`AddIdentity`, no cookie auth, no JWT bearer handler, no API keys, no
login endpoint. This overlaps with A01: since there's no concept of a
"user" anywhere in the data model or the request pipeline, there's
nothing to identify or authenticate in the first place.

Framed strictly against the OWASP category, this counts as
uncovered rather than not-applicable, because the category is about
whether identity is established and verified where the system's data
implies it should be — and a task list is inherently the kind of
resource that's normally scoped to a person. If this stays a
single-user local tool by design, this finding is accepted risk; if
multiple people are ever meant to use it, authentication has to be
designed in from the start, alongside the `UserId` scoping mentioned
under A01.

---

## A08:2025 — Software or Data Integrity Failures

**Status: ✅ Covered.**

(Package/dependency provenance now lives under A03's broader
supply-chain umbrella — this section covers the narrower "does the app
trust data/updates it shouldn't" question.)

- All deserialization goes through `System.Text.Json`'s default model
  binder — no `BinaryFormatter`, no custom `TypeNameHandling`-style
  polymorphic deserialization of untrusted input anywhere.
- Schema changes are integrity-checked by construction: EF Core
  migrations are generated files reviewed in a pull request before
  merge (per the git workflow in the root `CLAUDE.md` — no direct
  commits to `main`), so a schema change can't silently land without
  review.
- No auto-update or dynamic code-loading mechanism exists in the app
  itself that could be tricked into trusting an untrusted update.

---

## A09:2025 — Security Logging and Alerting Failures

**Status: ❌ Not covered.**

`appsettings.json` configures only the ASP.NET Core default logging
(`Logging:LogLevel:Default = Information`,
`Microsoft.AspNetCore = Warning`) — general request-pipeline logging,
not anything security-specific. There is:

- No audit trail of who created, changed, or deleted a task (there's
  no "who" to record in the first place — see A07).
- No logging around failed requests, validation failures, or anomalous
  patterns (e.g., a burst of 404s from `GetById`/`Update`/`Delete`
  probing IDs).
- No alerting integration of any kind — 2025 explicitly calls out
  *alerting*, not just logging, as part of this category, and there's
  nothing here that would notify anyone even if the default logs did
  capture something worth acting on.

For a local single-user tool this is low-stakes today, but it's an
absent control, not a scoped-down one — there's currently no way to
answer "what happened to this data and when" beyond whatever SQL
Server's own transaction log retains.

---

## A10:2025 — Mishandling of Exceptional Conditions

**Status: ⚠️ Partially covered.**

This category is new for 2025 (replacing SSRF, which moved under A01)
and covers how the app behaves when things go wrong — both "expected"
exceptional conditions the code anticipates, and unhandled ones it
doesn't.

Handled well:

- Expected not-found cases are handled explicitly and consistently:
  `GetById`, `Update`, and `Delete` all check `if (task is null) return
  NotFound();` rather than letting a null-reference exception surface
  (`Controllers/TasksController.cs`). A missing task produces a clean
  `404`, not a stack trace.
- Validation failures are handled by the framework, not ad hoc: the
  `[Required]` attributes on `CreateTaskDto`/`UpdateTaskDto` combined
  with `[ApiController]`'s automatic model-state check produce a
  structured `400 ValidationProblemDetails` response with field-level
  messages, without any controller code needing to check
  `ModelState.IsValid` itself.

Not hardened:

- There is no exception-handling middleware registered for
  non-Development environments — no `app.UseExceptionHandler(...)`, no
  `AddProblemDetails()`. An *unexpected* exception (a dropped database
  connection, a constraint violation, anything not explicitly checked
  for) has no configured handler to pass through in a hypothetical
  production run. It falls through to Kestrel's bare default, which
  returns a generic `500` with an empty body — so nothing sensitive
  leaks, but the response is unstructured and unshaped by deliberate
  choice rather than by design.
- In Development, `app.UseDeveloperExceptionPage()` is enabled
  (`Program.cs:30`), which is correct for local debugging — but it's
  worth being aware that if an EF Core exception ever surfaces (e.g., a
  failed connection to SQL Server), the exception text can include
  details like the server/instance name from the connection string.
  That's expected and fine in Development, but it's a reminder of why
  this page must never be reachable outside `IsDevelopment()` — which,
  correctly, it isn't (the middleware registration is inside the `if`
  block).

**To close this:** register `app.UseExceptionHandler(...)` (with
`AddProblemDetails()`) for non-Development environments so unhandled
exceptions produce a consistent, structured, non-leaky response instead
of relying on the hosting layer's bare fallback.
