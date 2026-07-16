# API Deep Dive: `/api`, for an ASP.NET Web API developer who's never used EF Core

This assumes you already know ASP.NET Web API cold — controllers,
routing, DI, DTOs, middleware pipeline, `appsettings.json`. It does
**not** assume any EF Core knowledge. Every section is built around one
question: "if I were writing this with raw ADO.NET or Dapper, what is
EF Core doing instead, and why?"

---

## 0. What EF Core actually is

Entity Framework Core is an ORM (object-relational mapper): you write
C# classes and LINQ queries, and it generates the SQL, executes it
against the connection, and materializes rows back into objects. The
three moving pieces you'll touch constantly in this repo:

| Concept | What it is | Where in this repo |
|---|---|---|
| Entity | A plain C# class mapped to a table | `Models/TaskItem.cs` |
| `DbContext` | A scoped "session" — connection + query surface + change tracker | `Data/TaskDbContext.cs` |
| Migration | A generated, versioned diff that evolves the DB schema to match your C# model | `Migrations/` |

If you've used Dapper: EF Core replaces your hand-written SQL strings
with LINQ that gets translated to SQL for you, *and* adds a layer
Dapper doesn't have at all — the **change tracker** (§4), which is
where most of the "magic"/confusion comes from.

---

## 1. The entity — `Models/TaskItem.cs`

```csharp
public class TaskItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsDone { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

Nothing here is EF-specific syntax — that's the point of EF Core's
**convention-based mapping**. No base class, no attributes, no
interface. EF infers the entire table shape from C# reflection plus a
few naming conventions:

- **A property named `Id` (or `TaskItemId`) is assumed to be the
  primary key.** No `[Key]` attribute needed. That's why `TaskItem.Id`
  became `Tasks.Id` PK with zero extra configuration.
- **Nullable reference types drive SQL nullability.** The project has
  `<Nullable>enable</Nullable>` in `TaskTracker.Api.csproj`. EF reads
  that: `string?` → nullable column, `required string` / non-nullable
  `string` → `NOT NULL` column. Look at the migration this produced
  (§3) — `Description` (`string?`) became `nullable: true`, `Title`
  (`required string`) became `nullable: false`. This is the C#-level
  source of truth for what your earlier `DESCRIBE TABLE` output showed
  in the live database.
- **`int Id` with no explicit value-generation config defaults to an
  identity column** on SQL Server — auto-incrementing, server-assigned.
  That's why `Create()` in the controller never sets `task.Id` — the
  database assigns it, and EF reads it back into the object after
  insert (visible in `CreatedAtAction(nameof(GetById), new { id =
  task.Id }, ...)` — `task.Id` is populated *after* `SaveChangesAsync()`
  returns, not before).

There's no `[Table("Tasks")]` attribute either — the table name comes
from the `DbSet` property name in the `DbContext` (§2), pluralization
convention aside since it's already named `Tasks`.

**Compare to Dapper:** with Dapper you'd write `SELECT Id, Title, ...
FROM Tasks WHERE Id = @id` by hand, get back a `TaskItem` via manual or
reflection-based mapping, and there'd be no compile-time link between
the class shape and the schema at all — a column rename wouldn't break
your build, just fail at runtime. EF's convention mapping is what lets
`dotnet build` catch a subset of those drift bugs.

---

## 2. The `DbContext` — `Data/TaskDbContext.cs`

```csharp
public class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
}
```

Two ideas to internalize:

**`DbContext` is a unit of work + a repository, scoped per-request.**
It wraps one logical database connection/session, tracks every entity
it has loaded or added during its lifetime, and batches all pending
writes until you explicitly call `SaveChangesAsync()`. Registered via:

```csharp
builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TaskTrackerDb")));
```

(`Program.cs`) — `AddDbContext` registers it with a **scoped** lifetime
by default, meaning ASP.NET creates one instance per HTTP request and
disposes it at the end, the same lifetime you'd manually choose for a
hand-rolled `IDbConnection` factory in a Dapper setup. `TasksController`
receives it via primary-constructor DI —
`public class TasksController(TaskDbContext db) : ControllerBase` — no
different from injecting any other scoped service.

**`DbSet<TaskItem> Tasks`** is your query/write entry point for the
`Tasks` table — think of it as a strongly-typed, LINQ-queryable
`IRepository<TaskItem>` that EF Core hands you for free, with no
interface to implement. `db.Tasks` is where every operation in
`TasksController` starts.

This `DbContext` is deliberately tiny — one `DbSet`, no configuration
overrides (no `OnModelCreating`). Larger EF Core apps often add
`OnModelCreating(ModelBuilder modelBuilder)` to configure things
conventions can't infer (composite keys, indexes, relationships,
max-length constraints) — this app doesn't need it because the schema
is simple and conventions cover everything.

---

## 3. Migrations — code-first schema evolution

**The core idea:** you never write `CREATE TABLE` or `ALTER TABLE` by
hand. You change the C# entity, then ask EF's tooling to *diff* your
new model against the last known model and generate the SQL to close
the gap. That diff is a "migration."

```
cd api
dotnet ef migrations add SomeChangeName
```

This does NOT touch the database. It only touches C# files in
`Migrations/`. Compare to a raw-SQL workflow where you'd hand-write a
versioned `.sql` script and a tool like Flyway/DbUp applies it — EF's
migration *is* that script, just expressed as C# calls instead of raw
SQL, so it can run against whatever provider you're targeting.

This repo has exactly one migration,
`Migrations/20260711111314_InitialCreate.cs`:

```csharp
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Tasks",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsDone = table.Column<bool>(type: "bit", nullable: false),
                DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_Tasks", x => x.Id); });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Tasks");
    }
}
```

- **`Up`** — what to run to move the schema *forward* to this
  migration's state (here: create the whole table, since it's the
  first migration).
- **`Down`** — the exact inverse, for rolling back
  (`dotnet ef database update <PreviousMigrationName>` or
  `dotnet ef migrations remove`). EF generates both automatically from
  the model diff; you rarely hand-edit either.

Applying it to an actual database is a separate, explicit step:

```
dotnet ef database update
```

This is the command from the root `CLAUDE.md`'s "How to run" section.
It connects using the configured connection string, checks a bookkeeping
table (`__EFMigrationsHistory` — this is the second table you saw when
you listed tables in `TaskTrackerDb` earlier) to see which migrations
have already run, and applies any that haven't, in order, by literally
executing their `Up()` method's SQL.

### The two files you didn't ask about but will see every time

Each migration actually produces **two** files, plus updates a third:

- **`20260711111314_InitialCreate.cs`** — the `Up`/`Down` you write or
  review (shown above).
- **`20260711111314_InitialCreate.Designer.cs`** — auto-generated, don't
  hand-edit. It's a frozen snapshot of what the *entire* model looked
  like at the moment this migration was created, used internally by EF's
  tooling to compute the diff for the *next* migration. This is the file
  that actually enumerates every property/type/nullability the way you
  saw for `TaskItem` — it's metadata, not something you write.
- **`TaskDbContextModelSnapshot.cs`** — one file per `DbContext` (not
  per migration), always reflecting the *current, cumulative* model
  state after every migration applied so far. When you run
  `migrations add` again later, EF diffs your live `TaskItem` class
  against *this* file, not against the database, to figure out what
  changed.

**This is the critical mental model:** EF Core's migration diffing is
entirely **model-vs-model** (your current C# classes vs. the last
snapshot), never **model-vs-live-database**. That's exactly why
`CLAUDE.md` says "model changes go through `dotnet ef migrations add`
— never hand-edit the database schema": if you manually `ALTER TABLE`
in SQL Server Management Studio, the snapshot file has no idea that
happened. The next `migrations add` will diff against the stale
snapshot, not your actual schema, and generate a migration that's wrong
relative to reality — EF has no way to detect the drift.

You verified this "no drift" state directly earlier in this project — the
live `Tasks` table matched `TaskDbContextModelSnapshot.cs` and
`TaskItem.cs` exactly, which is the state you always want to be in.

---

## 4. Change tracking — the biggest EF-specific concept

This is the thing with no ADO.NET/Dapper equivalent at all, and it's
the source of most EF confusion. Compare the two read paths in
`TasksController`:

```csharp
// GetAll / GetById
var tasks = await db.Tasks.AsNoTracking().Select(t => ToDto(t)).ToListAsync();

// Update
var task = await db.Tasks.FindAsync(id);
```

**Without `AsNoTracking()`**, every entity a query returns gets
registered with the `DbContext`'s internal `ChangeTracker`, which keeps
a snapshot of each tracked entity's original property values. From that
point on, any mutation you make to that object in C# — `task.Title =
dto.Title;` — is silently noticed by the tracker as a diff against the
snapshot.

Then `SaveChangesAsync()`:

1. Walks every tracked entity.
2. For each one, compares current values vs. the original snapshot.
3. Generates `UPDATE`/`INSERT`/`DELETE` statements only for what
   actually changed, and only for entities that are dirty.

That's the entire mechanism behind `Update()` in `TasksController`:

```csharp
var task = await db.Tasks.FindAsync(id);
if (task is null) return NotFound();

task.Title = dto.Title;
task.Description = dto.Description;
task.IsDone = dto.IsDone;
task.DueDate = dto.DueDate;
await db.SaveChangesAsync();
```

There is no explicit `db.Tasks.Update(task)` call anywhere — it would
be redundant. `FindAsync` returned a *tracked* entity; mutating its
properties directly is enough. `SaveChangesAsync()` diffs it, sees the
changed columns, and emits exactly one `UPDATE Tasks SET Title = @p0,
Description = @p1, IsDone = @p2, DueDate = @p3 WHERE Id = @p4`.

`Delete()` follows the same pattern but via `db.Tasks.Remove(task)`,
which explicitly marks a tracked entity for deletion (there's no
"just delete it by mutating a property" equivalent for removal, since
deletion isn't a property diff).

**Why `GetAll`/`GetById` use `AsNoTracking()` instead:** those are pure
reads — the entity is projected straight into a `TaskDto` and returned;
nothing is ever mutated and saved back. Tracking it would cost memory
and CPU (maintaining a snapshot, diffing on every `SaveChanges` call
elsewhere in the same request) for zero benefit. `AsNoTracking()` tells
EF "materialize this and hand it to me, but don't bother watching it."
This is a pure performance optimization for read-only queries — a
convention worth applying by default to any query result you won't
mutate and save.

**The failure mode to know:** if `Update()`'s `FindAsync` were changed
to include `.AsNoTracking()`, the entity returned wouldn't be watched
by the change tracker at all. The controller would still mutate
`task.Title`, etc. in memory, but `SaveChangesAsync()` would find
*nothing* dirty in the tracker and silently execute zero SQL. No
exception, no error — the endpoint returns `204 No Content` (success)
and the database is untouched. This is the sharpest edge in EF Core for
someone new to it: tracked-vs-not is invisible at the call site unless
you know to look for `AsNoTracking()`.

---

## 5. LINQ → SQL translation

```csharp
var tasks = await db.Tasks.AsNoTracking().Select(t => ToDto(t)).ToListAsync();
```

`db.Tasks` is an `IQueryable<TaskItem>`, not an in-memory collection.
Each LINQ method chained onto it (`.Where`, `.Select`, `.FirstOrDefaultAsync`,
etc.) doesn't run in C# — it builds up an **expression tree**, a
data structure describing the query, without executing anything. EF
Core's SQL Server provider walks that expression tree and translates it
into an actual `SELECT` statement, sent to the database only when you
call something that forces execution — `ToListAsync()`,
`FirstOrDefaultAsync()`, `CountAsync()`, etc. ("deferred execution.")

```csharp
var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
```

becomes, roughly, `SELECT TOP(1) * FROM Tasks WHERE Id = @id` —
parameterized automatically (no manual SQL injection risk the way
string-concatenated ADO.NET/Dapper queries can have).

**The catch that bites people new to EF:** not every C# expression can
be translated to SQL. Call a C# method the SQL provider doesn't
recognize inside a `Where`/`Select`, and you'll get a runtime exception
(`could not be translated`) rather than a compile error — because the
compiler has no way to know in advance whether SQL Server's provider
can express your LINQ in T-SQL. This codebase's queries are simple
enough (equality filters, direct property projection) that this never
comes up, but it's the reason EF LINQ isn't "just C# that happens to
run on a database" — it's closer to "a C#-shaped query language that
gets recompiled to SQL, with a smaller surface than full C#."

`FindAsync(id)` (used in `Update`/`Delete`) is a shortcut, not a LINQ
query — it looks up by **primary key** specifically, and it first
checks whether an entity with that key is *already tracked in this
context* before hitting the database at all. For a single call per
request like here, that distinction rarely matters; it matters more in
code that calls `FindAsync` on the same ID multiple times within one
request.

---

## 6. DTOs — the same discipline, now visible from the EF side

You already know *why* `TasksController` maps `TaskItem` → `TaskDto`
(wire-contract stability, no leaking tracking state — see
`Dtos/TaskDto.cs`, `CreateTaskDto.cs`, `UpdateTaskDto.cs`). The EF-specific
angle worth adding: `AsNoTracking().Select(t => ToDto(t))` performs the
entity→DTO projection **inside the LINQ expression**, before
`ToListAsync()` materializes anything. EF's SQL translation is smart
enough to turn that into a `SELECT` that only pulls the columns
`ToDto()` actually reads — in this case that's everything on `TaskItem`
anyway since `TaskDto` mirrors it 1:1, but if `TaskDto` only exposed
`Title` and `IsDone`, this same pattern would generate a narrower
`SELECT Title, IsDone FROM Tasks` instead of `SELECT *`. Projecting
early, before materialization, is generally cheaper than loading full
entities and mapping in C# afterward.

`CreateTaskDto`/`UpdateTaskDto` carry `[Required(ErrorMessage = "Title
is required.")]` — standard ASP.NET model validation, nothing
EF-specific, enforced by `[ApiController]`'s automatic
`400 BadRequest` on invalid `ModelState`, same as any other Web API
project. Note this validates at the **DTO** level, before an entity is
ever constructed, so an EF entity with a non-nullable `required string
Title` never has a chance to be constructed in an invalid state from a
bad request.

---

## 7. Connection strings and providers

`Program.cs`:

```csharp
builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TaskTrackerDb")));
```

`UseSqlServer(...)` selects the SQL Server **provider** — the piece
translating LINQ to T-SQL specifically (as opposed to `UseSqlite`,
`UseNpgsql`, `UseInMemoryDatabase`, etc.). The connection string comes
from standard ASP.NET configuration (`IConfiguration`), nothing
EF-specific:

```json
// appsettings.Development.json (gitignored — see root CLAUDE.md "Secrets")
"ConnectionStrings": {
  "TaskTrackerDb": "Server=localhost\\SQLEXPRESS;Database=TaskTrackerDb;Trusted_Connection=True;TrustServerCertificate=True"
}
```

`appsettings.json` (committed) has no `ConnectionStrings` section at
all — the real one only exists in the gitignored
`appsettings.Development.json`, same secrets-handling convention you'd
use for any other credential in an ASP.NET app.

---

## 8. Testing without a real database

`TaskTracker.Api.Tests/ApiWebApplicationFactory.cs`:

```csharp
public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<TaskDbContext>)
                    || d.ServiceType == typeof(DbContextOptions)
                    || d.ServiceType == typeof(IDbContextOptionsConfiguration<TaskDbContext>))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            services.AddDbContext<TaskDbContext>(options =>
                options.UseInMemoryDatabase($"TaskTrackerTestDb-{Guid.NewGuid()}"));
        });
    }
}
```

`WebApplicationFactory<Program>` boots the *real* app in-process for
integration tests — nothing EF-specific about that part, it's standard
ASP.NET test infrastructure (this is why `Program.cs` ends with
`public partial class Program { }`: making the implicit top-level
`Program` class `partial` and non-`internal` is required for the test
project to reference it as a type parameter here).

The EF-specific part: `ConfigureServices` surgically removes the
production `DbContextOptions<TaskDbContext>` registration (the one
pointing at real SQL Server, registered in `Program.cs`) and replaces
it with `UseInMemoryDatabase(...)` — EF Core's **in-memory provider**, a
different provider entirely from `UseSqlServer`, purpose-built for
testing. It behaves like a real database for basic CRUD (supports
`DbSet`, change tracking, `SaveChangesAsync`) but isn't SQL Server at
all — no real T-SQL runs, no installation needed, and a fresh
`Guid`-named instance per test class means tests don't leak state into
each other. This is what root `CLAUDE.md` means by "Runs the xUnit
suite... against an EF Core in-memory database — no SQL Server instance
required."

**Worth knowing as a limitation:** the in-memory provider doesn't
enforce everything a real relational database would (e.g., it won't
catch a SQL Server-specific constraint violation or translate LINQ
exactly the way the SQL Server provider does for edge cases). It's
good for testing controller/business logic against realistic
CRUD behavior, not for verifying your queries actually work against
real T-SQL — that's what running the app against actual SQL Server
locally is for.

---

## Summary: one full write request, start to finish

Tying every section together — `PUT /api/tasks/3` marking a task done:

1. ASP.NET routes the request to `TasksController.Update(3, dto)` —
   ordinary Web API routing, nothing EF-specific (§0).
2. `[ApiController]` validates `UpdateTaskDto` against its
   `[Required]` attributes before the action even runs (§6).
3. `TaskDbContext db` was injected as a **scoped** service — one
   instance for this whole request (§2).
4. `db.Tasks.FindAsync(3)` — EF checks its change tracker for an
   already-tracked entity with PK `3` first; finding none, it builds
   and sends `SELECT TOP(1) * FROM Tasks WHERE Id = @p0`, materializes
   the row into a `TaskItem`, and **registers it with the change
   tracker**, snapshotting its current values (§4, §5).
5. The controller mutates `task.Title`, `task.IsDone`, etc. directly —
   plain C# property sets, no EF API call (§4).
6. `db.SaveChangesAsync()` — the change tracker diffs the entity
   against its snapshot, finds the changed columns, and executes one
   parameterized `UPDATE Tasks SET ... WHERE Id = @p0` (§4).
7. Controller returns `204 No Content` — standard ASP.NET, unrelated to
   EF.

And the schema all of this depends on didn't come from anyone running
`ALTER TABLE` — it came from `TaskItem.cs`'s shape, frozen into
`20260711111314_InitialCreate.cs` via `dotnet ef migrations add`, and
applied to the real database via `dotnet ef database update` (§3).
