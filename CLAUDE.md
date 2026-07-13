# Task Tracker

Full-stack task tracker: .NET Web API backend, Angular frontend, SQL Server database.

## Layout

- `/api` — C# .NET 10 Web API (controllers + EF Core), talks to SQL Server
- `/web` — Angular 22 single-page app that calls the API
- Database — SQL Server 2025 Express, database `TaskTrackerDb`, table `Tasks`
  (`Id`, `Title`, `Description`, `IsDone`, `DueDate`, `CreatedAt`)

## Conventions

### API (`/api`)

- EF Core, code-first with migrations. Model changes go through
  `dotnet ef migrations add <Name>` — never hand-edit the database schema.
- Enable CORS for the Angular dev server origin (`http://localhost:4200`
  and `https://localhost:4200`).
- Controllers return DTOs, never EF entities directly (avoids leaking tracking
  state / over-posting, keeps the wire contract stable if the entity changes).

### Web (`/web`)

- Standalone components (no NgModules).
- API access goes through a typed `TaskService` wrapping `HttpClient` —
  components don't call `HttpClient` directly.

### Secrets

- Never commit secrets. Connection strings live in
  `appsettings.Development.json` (gitignored), not `appsettings.json`.

### Git workflow

- Never commit directly to `main`.
- One feature branch per feature, named `feat/<short-description>`.
- Commit after each logical change with a clear Conventional Commits message
  (`feat:`, `fix:`, `refactor:`, etc.) — don't batch everything into one
  commit at the end.
- Before each commit, show the diff and the proposed commit message for
  review.
- After pushing, open a PR with the host's CLI:
  - GitHub: `gh pr create --fill`
  - Azure DevOps: `az repos pr create`

## How to run

### Database

Ensure SQL Server 2025 Express is running locally, then apply migrations from `/api`:

```
cd api
dotnet ef database update
```

### API

```
cd api
dotnet run
```

Runs on `http://localhost:5189` by default (the `http` launch profile — this
is also what the Angular dev proxy expects). Use
`dotnet run --launch-profile https` only if you need TLS directly
(`https://localhost:7062`); that profile forces HTTP→HTTPS redirects, which
breaks the Angular proxy, so use the default `http` profile when running the
API and the Angular dev server together. The `https` profile needs a
trusted dev cert once per machine: `dotnet dev-certs https --trust`.

Swagger UI (Development only): `http://localhost:5189/swagger/index.html`.

### API tests

```
cd api
dotnet test
```

Runs the xUnit suite in `TaskTracker.Api.Tests` against an EF Core
in-memory database — no SQL Server instance required.

### Web

```
cd web
npm install
ng serve
```

Runs at `https://localhost:4200` (HTTPS by default — see `angular.json`'s
`serve.options`). The dev server proxies `/api/*` requests to the API at
`http://localhost:5189` (see `proxy.conf.json`), so the Angular environment
files just set `apiUrl: '/api'` — no host/port to configure there.

The HTTPS cert/key aren't committed (`web/.cert/`, gitignored) — export the
same trusted ASP.NET Core dev cert once per machine so the browser trusts
`https://localhost:4200` without a warning:

```
cd web
mkdir .cert
dotnet dev-certs https --export-path .cert/localhost.pem --format Pem --no-password
```

(Produces `.cert/localhost.pem` and `.cert/localhost.key`, matching the
paths already configured in `angular.json`.)

### Web tests

```
cd web
ng test
```

Runs the Vitest suite (components and `TaskService`).
