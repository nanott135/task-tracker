# API conventions (`/api`)

- EF Core, code-first with migrations. Model changes go through
  `dotnet ef migrations add <Name>` — never hand-edit the database schema.
- Enable CORS for the Angular dev server origin (`http://localhost:4200`
  and `https://localhost:4200`).
- Controllers return DTOs, never EF entities directly (avoids leaking tracking
  state / over-posting, keeps the wire contract stable if the entity changes).

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
