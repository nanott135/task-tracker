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
- Enable CORS for the Angular dev server origin `http://localhost:4200`.
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

### Web

```
cd web
npm install
ng serve
```

Runs at `http://localhost:4200` and calls the API (configure the API base URL
in the Angular environment files).
