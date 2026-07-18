# Web conventions (`/web`)

- Standalone components (no NgModules).
- API access goes through a typed `TaskService` wrapping `HttpClient` —
  components don't call `HttpClient` directly.

## How to run

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
