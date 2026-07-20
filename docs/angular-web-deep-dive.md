# Angular Deep Dive: the `/web` app, for an ASP.NET developer

This walks through every file in `/web` that matters, in the order the
app actually executes them, translating each Angular concept to its
closest ASP.NET / C# equivalent. It assumes zero prior Angular
knowledge, but assumes you're comfortable with: HTML, calling a REST
API, DI containers, async code, and general SPA concepts.

Angular version in this repo: 22, using **standalone components** (no
`NgModule`) and **signals** (Angular's newer reactive primitive,
replacing a lot of what you'd previously do with RxJS or Zone.js change
detection). If you've seen older Angular tutorials with `NgModule` and
`*ngIf`/`*ngFor`, this codebase uses the modern equivalents instead —
noted inline below.

---

## 0. The 30-second mental model

| ASP.NET concept | Angular equivalent | Where in this repo |
|---|---|---|
| `Program.cs` (entry point, DI setup) | `main.ts` + `app.config.ts` | §1 |
| DI container (`builder.Services.AddScoped<T>()`) | Angular's injector (`@Injectable`, `inject()`) | §3 |
| Controller | Component (`.ts` + `.html` pair) | §2, §4 |
| Razor view / `.cshtml` | Component template (`.html`) | §4 |
| DTO class | TypeScript `interface` | §6 |
| `HttpClient` (typed client wrapping calls) | `HttpClient` (yes, same name, same idea) | §7 |
| `Task<T>` / `async`-`await` | RxJS `Observable<T>` / `.subscribe()` | §7 |
| A field with a backing `INotifyPropertyChanged` | `signal<T>()` | §5 |
| `launchSettings.json` (per-profile URLs) | `angular.json` (`serve` config) + `proxy.conf.json` | §8 |
| `appsettings.{Environment}.json` | `environment.ts` / `environment.development.ts` | §8 |
| xUnit + `WebApplicationFactory` | Vitest + `TestBed` | §9 |

Keep this table open while reading — every section below expands one row.

---

## 1. Bootstrapping — `main.ts` and `app.config.ts`

There is no `Startup.cs`-style single entry class here. Two files split
that job:

**`src/main.ts`** — the actual entry point, analogous to `Program.cs`'s
`var app = builder.Build(); app.Run();`:

```ts
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
```

This says: "render the `App` component into the page, using the
services/config in `appConfig`." That's the entire startup sequence —
there's no separate build-the-host step like ASP.NET's `WebHost`,
because the "host" is just the browser tab.

**`src/app/app.config.ts`** — analogous to the `builder.Services.Add...`
calls in `Program.cs`:

```ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient()
  ]
};
```

Each `provideX()` call registers something with Angular's DI container,
the same way `builder.Services.AddControllers()` or
`builder.Services.AddHttpClient()` register ASP.NET middleware/services.
The one that matters most here is `provideHttpClient()` — without it,
nothing in the app could inject `HttpClient` at all, and you'd get a
runtime DI error the first time something tried.

`provideRouter(routes)` wires up client-side routing. In this repo,
`app.routes.ts` is just:

```ts
export const routes: Routes = [];
```

Empty — this app is a single view with no URL-based navigation yet.
Think of it as an ASP.NET app with exactly one controller action and no
attribute routing beyond the default.

---

## 2. The component tree

Angular apps are a tree of components, each owning a slice of the DOM.
This app's tree is exactly two components deep:

```
App (app-root)
 └── TaskList (app-task-list)
```

**`src/app/app.ts`** — the root component:

```ts
@Component({
  selector: 'app-root',
  imports: [TaskList],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {}
```

- `selector: 'app-root'` — the custom HTML tag this component renders
  as. Somewhere in `index.html` (not shown above, but it exists) there's
  a literal `<app-root></app-root>` tag; Angular replaces it with this
  component's rendered output. This is conceptually like a Razor
  `<component>` tag helper, except the whole page is one.
- `imports: [TaskList]` — standalone components must explicitly list
  every other component/directive/pipe they use in their template. This
  replaces the old `NgModule.declarations` array. It's the compiler's
  way of checking "you used `<app-task-list>` in your HTML — is that
  actually imported here?" at compile time, similar to how a C# file
  needs a `using` for a type it references.
- `templateUrl` / `styleUrl` — point at the paired `.html` and `.css`
  files. (Some Angular components inline the template as a string
  instead; this repo uses separate files, closer to the Razor
  `.cshtml` + code-behind split.)

`src/app/app.html` is one line:

```html
<app-task-list />
```

So `App` does nothing except host `TaskList`. All actual behavior lives
one level down.

---

## 3. Dependency Injection — `@Injectable` and `inject()`

This is one of the trickier parts of Angular's mental model, so
let's be very explicit.

**`src/app/services/task.service.ts`:**

```ts
@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly http = inject(HttpClient);
  ...
}
```

Two things happening:

1. `@Injectable({ providedIn: 'root' })` — this registers `TaskService`
   with the app's root injector as a **singleton**: the injector builds
   it once, the first time anything asks for it, and every subsequent
   request gets that same instance. This is the direct equivalent of
   `builder.Services.AddSingleton<TaskService>()`, except you don't
   write that registration line anywhere separately — the decorator on
   the class *is* the registration. (Angular also supports
   `providedIn: 'root'`'s cousins for scoped/transient-like lifetimes if
   a component supplies its own provider, but this repo doesn't use
   that.)

2. `inject(HttpClient)` — this is how the class receives its
   dependency. It's functionally identical to constructor injection —
   `public TaskService(HttpClient http)` in C# — just spelled as a
   function call assigned to a field instead of a constructor
   parameter. `inject()` only works while Angular is actively
   constructing the class (or in a couple of other specific contexts),
   the same way constructor injection only works during DI-driven
   construction.

Then in `task-list.ts`:

```ts
export class TaskList implements OnInit {
  private readonly taskService = inject(TaskService);
  ...
}
```

`TaskList` doesn't `new TaskService()` — it asks the injector for the
shared instance, same singleton every time, same idea as a controller
declaring `TaskDbContext db` in its constructor and trusting the
container to supply it.

**What breaks without `providedIn: 'root'`:** if you deleted that
option (leaving bare `@Injectable()`), the class would no longer
register itself anywhere. `inject(TaskService)` would throw
`NullInjectorError: No provider for TaskService!` at runtime — the
Angular equivalent of `InvalidOperationException: Unable to resolve
service for type 'TaskService'` when a service was never added to
`IServiceCollection`.

### Other injection lifetimes — scoped, and the missing transient

`providedIn: 'root'` is Angular's `AddSingleton<T>()`, but the mapping
to ASP.NET Core's three lifetimes isn't 1:1. This repo only uses the
singleton form, but it's worth knowing what else exists:

- **Component-level providers** are the closest thing to "scoped."
  Instead of `providedIn: 'root'` on the service, a component declares
  `providers: [TaskService]` in its `@Component` decorator. That
  creates a *new instance per component instance*, shared by that
  component and its children, destroyed when the component is. The
  gotcha: if the component renders multiple times (e.g. inside an
  `@for` loop), **each rendered instance gets its own separate service
  instance** — there's no ASP.NET Core equivalent to "scoped per
  widget on the page." Think of it as scoped to a subtree of the
  component tree, not to an HTTP request.
- **Route-level providers** — a route config can declare its own
  `providers: [...]`, scoping an instance to that route, torn down on
  navigation away. Not relevant yet here since `app.routes.ts` (§1) is
  currently an empty array — no routing is live in this repo.
- **No true Transient exists.** There's no built-in "brand-new
  instance on every `inject()` call" lifetime. Every injector (root, a
  component's, a route's) resolves a token *once* and caches it for
  everything downstream in that injector's scope — `inject()` always
  returns "the cached instance for this scope," never a fresh one. If
  you genuinely need a new object per call, the idiomatic pattern is
  injecting a *factory* service (itself a singleton) whose method does
  `new` internally, rather than relying on a lifetime setting.

---

## 4. Templates — the HTML side of a component

`src/app/task-list/task-list.html` is the view for `TaskList`. If
you've used Razor, this will feel structurally familiar but with
different syntax for every dynamic bit. Four binding types, all present
in this one file:

### Interpolation — `{{ }}`
```html
<span class="task-title">{{ task.title }}</span>
```
Renders a value as text. Equivalent to Razor's `@task.Title`.

### Property binding — `[ ]`
```html
<li class="task" [class.done]="task.isDone">
<input type="checkbox" [checked]="task.isDone" />
```
Binds a DOM property to a TypeScript expression. `[class.done]="..."`
conditionally applies the CSS class `done` when the expression is
truthy — like a C# ternary building a class string, but declarative.

### Event binding — `( )`
```html
<button type="button" (click)="load()">Retry</button>
<form class="add-form" (ngSubmit)="addTask()" novalidate>
```
Wires a DOM event to a component method call. `(click)="load()"` is
directly analogous to wiring up an `onclick` handler, except type-checked
against the component class — the compiler verifies `load()` actually
exists on `TaskList`.

### Two-way binding — `[( )]` ("banana in a box")
```html
<input name="title" type="text" [(ngModel)]="newTitle" />
```
Combines property + event binding: the input's value initializes from
`newTitle`, and every keystroke writes back into `newTitle`. This is
what `FormsModule` (imported in `task-list.ts`'s `imports: [FormsModule,
...]`) provides — without importing `FormsModule`, `ngModel` isn't
recognized in the template at all, the same way you'd get a compile
error using a Tag Helper without its assembly referenced. See the
Appendix for what `ngModel` actually is under the hood, including why
it needs a `name` attribute inside a `<form>`.

### Control flow — `@if` / `@for`
```html
@if (loading()) {
  <p class="status">Loading tasks…</p>
} @else if (tasks().length === 0) {
  <p class="status">No tasks yet — add one above.</p>
} @else {
  <ul class="task-list">
    @for (task of tasks(); track task.id) {
      <li class="task" [class.done]="task.isDone"> ... </li>
    }
  </ul>
}
```
This is Angular's modern built-in control-flow syntax (`@if`/`@for`),
introduced to replace the older `*ngIf`/`*ngFor` structural directives
you'll see in most existing tutorials/StackOverflow answers — if you're
reading older material, mentally translate `*ngIf="cond"` to `@if
(cond) { }`. `track task.id` is required on `@for` and tells Angular
how to identify each item across re-renders (so it can diff efficiently
instead of re-rendering the whole list) — same purpose as a `key` prop
in React, or as giving EF Core a primary key to track entity identity
by.

### Pipes — `|`
```html
{{ task.dueDate | date: 'mediumDate' }}
```
A pipe transforms a bound value for display, without changing the
underlying data. `date` is a built-in pipe (imported via `DatePipe` in
`task-list.ts`'s `imports` array) — the rough equivalent of calling
`task.DueDate.ToString("d")` inline in a Razor view, but reusable and
declared once.

---

## 5. State — `signal()`

**`src/app/task-list/task-list.ts`:**

```ts
readonly tasks = signal<Task[]>([]);
readonly loading = signal(false);
readonly error = signal<string | null>(null);
```

A `signal` wraps a value in a reactive container:

- **Read** it by calling it as a function: `tasks()`, `loading()`.
- **Write** it with `.set(newValue)` or `.update(fn)`:
  ```ts
  this.tasks.set(tasks);
  this.tasks.update((tasks) => [...tasks, task]);
  ```

The key behavior: any template expression that reads a signal (e.g.
`@if (loading())`) automatically re-runs whenever that signal's value
changes. There's no manual "refresh the UI" step, no `INotifyPropertyChanged`
event to raise by hand — reading the signal inside the template is
enough to subscribe the view to it.

Compare to a C# property with `INotifyPropertyChanged`:

```csharp
private bool _loading;
public bool Loading
{
    get => _loading;
    set { _loading = value; OnPropertyChanged(nameof(Loading)); }
}
```

`signal()` gives you that same "changing this notifies observers"
behavior, but built into the primitive itself — no boilerplate event,
no interface to implement.

Note `newTitle`, `newDescription`, `newDueDate` in the same class are
**plain fields, not signals**:

```ts
newTitle = '';
newDescription = '';
newDueDate = '';
```

That's intentional — they're only ever read/written through
`[(ngModel)]` two-way binding on form inputs, which handles its own
change detection for form controls. Signals are for state the
*template's control flow or display logic* reacts to; plain fields are
fine for simple form-input backing values.

---

## 6. Models — `models/task.ts`

```ts
export interface Task {
  id: number;
  title: string;
  description: string | null;
  isDone: boolean;
  dueDate: string | null;
  createdAt: string;
}

export interface CreateTask {
  title: string;
  description: string | null;
  isDone: boolean;
  dueDate: string | null;
}

export type UpdateTask = CreateTask;
```

These are the client-side mirror of the API's DTOs
(`api/Dtos/TaskDto.cs`, `CreateTaskDto.cs`, etc.) — same idea as sharing
a contract between client and server, except there's no code generation
here; someone hand-wrote these interfaces to match the C# DTO shapes.

Two things worth flagging, since they're easy to get wrong when a
backend contract changes:

- **`dueDate`/`createdAt` are typed `string`, not `Date`.** JSON has no
  date type — ASP.NET serializes `DateTime`/`DateTime?` as ISO 8601
  strings, and `HttpClient` here does *not* auto-parse them back into
  JS `Date` objects (unlike `System.Text.Json` deserializing straight
  into a C# `DateTime`). The `date` pipe used in the template
  (`task.dueDate | date: 'mediumDate'`) accepts an ISO string directly,
  so this works without extra parsing — but if you ever needed to do
  date arithmetic in TypeScript, you'd have to `new Date(task.dueDate)`
  yourself first.
- **These interfaces are a compile-time-only contract.** Unlike C#
  where `JsonSerializer.Deserialize<TaskDto>` will throw if the JSON
  genuinely can't map, TypeScript interfaces are erased at compile time
  — `http.get<Task[]>(...)` doesn't validate the response shape at
  runtime at all. If the API's JSON drifted from this interface (a
  renamed field, a type change), nothing would error; you'd just get
  `undefined` at the point of use, silently.

---

## 7. Calling the API — `HttpClient` and `Observable`

**`src/app/services/task.service.ts`**, in full:

```ts
@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/tasks`;

  getAll(): Observable<Task[]> {
    return this.http.get<Task[]>(this.baseUrl);
  }

  create(task: CreateTask): Observable<Task> {
    return this.http.post<Task>(this.baseUrl, task);
  }

  update(id: number, task: UpdateTask): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, task);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
```

See the Appendix for a line-by-line anatomy of the `@Injectable`/
`export class`/field-initializer declaration at the top of this file
— including why there's no constructor.

This is the repo's enforced convention (see root `CLAUDE.md`): **no
component calls `HttpClient` directly** — everything routes through
this typed service, mirroring why the API layer routes everything
through DTOs rather than raw entities. One seam, one place to change if
the base URL or error handling needs to evolve.

The `<Task[]>`, `<Task>`, `<void>` generics tell TypeScript what shape
to *assume* the JSON deserializes into (again — assumed, not validated;
see §6). Get this wrong (say, `getAll(): Observable<any>`) and you lose
compile-time checking on every property access downstream — a typo
like `tasks[0].titel` would silently compile and just render blank at
runtime instead of failing the build.

### The `Observable` vs `Task<T>` difference that actually matters

This is the biggest "gotcha" coming from C#. An `Observable` is **lazy**
— nothing happens when you call `getAll()`. No HTTP request is sent
until something calls `.subscribe()` on the result. Compare:

```csharp
// C#: the request fires as soon as you call GetAsync — 
// the Task is already "hot," running in the background.
var tasks = await httpClient.GetFromJsonAsync<List<TaskDto>>(url);
```

```ts
// Angular: nothing has happened yet. getAll() just built
// a description of a request.
const obs = this.taskService.getAll();

// THIS is what actually sends it:
obs.subscribe({
  next: (tasks) => { /* success */ },
  error: (err) => { /* failure */ },
});
```

In `task-list.ts`:

```ts
load(): void {
  this.loading.set(true);
  this.error.set(null);
  this.taskService.getAll().subscribe({
    next: (tasks) => {
      this.tasks.set(tasks);
      this.loading.set(false);
    },
    error: () => {
      this.error.set('Could not load tasks. Is the API running?');
      this.loading.set(false);
    },
  });
}
```

`next` is your success continuation (like the code after a successful
`await`); `error` is your catch block. There's no `finally` shown here,
but `Subscribable` supports a third `complete` callback for
"the stream ended with no error" — for a single HTTP call, `next` then
immediate completion is the normal path, which is why `loading.set(false)`
is duplicated in both `next` and `error` here rather than factored into
a single completion handler.

`load()` itself is called from `ngOnInit()`:

```ts
export class TaskList implements OnInit {
  ...
  ngOnInit(): void {
    this.load();
  }
```

`ngOnInit` is a **lifecycle hook** — Angular calls it once, automatically,
right after the component has been constructed and its inputs set, but
before it's first rendered to the DOM. This is the standard place to
kick off an initial data load — the closest ASP.NET analogy is
overriding `OnInitializedAsync()` in a Blazor component, if you've
touched Blazor; if not, just think "constructor, but guaranteed to run
after DI has finished wiring the object up."

### Optimistic update pattern — `toggleDone`

Worth calling out since it's a real-world pattern you'll hit often:

```ts
toggleDone(task: Task): void {
  const updated = { ...task, isDone: !task.isDone };
  this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? updated : t)));

  this.taskService
    .update(task.id, { title: updated.title, description: updated.description,
                        isDone: updated.isDone, dueDate: updated.dueDate })
    .subscribe({
      error: () => {
        this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? task : t)));
        this.error.set('Could not update the task. Please try again.');
      },
    });
}
```

The UI flips the checkbox **immediately** (`this.tasks.update(...)`),
before the API call even resolves — this is "optimistic UI": assume
success, update the screen instantly for responsiveness, and only if
the `error` callback fires, roll the local state back to the original
`task` and show an error banner. There's no `next` callback here at
all — on success, the optimistic update was already correct, so there's
nothing left to do. See the Appendix for a line-by-line walkthrough of
this method.

---

## 8. Environments and the dev proxy

**The problem:** the Angular dev server runs on `https://localhost:4200`;
the .NET API runs on `http://localhost:5189`. Different origins. A
naive `fetch('http://localhost:5189/api/tasks')` from the browser would
be a cross-origin request, and you'd need to configure CORS on the API
to allow it.

**The fix used here: a dev-time reverse proxy**, not CORS.

`src/environments/environment.ts`:
```ts
export const environment = { apiUrl: '/api' };
```

`TaskService.baseUrl` becomes `/api/tasks` — a same-origin, relative
URL. The browser requests `https://localhost:4200/api/tasks`.

`proxy.conf.json`:
```json
{
  "/api": {
    "target": "http://localhost:5189",
    "secure": false,
    "changeOrigin": true
  }
}
```

`angular.json`'s `serve.options` points `ng serve` at this file
(`"proxyConfig": "proxy.conf.json"`). When the dev server sees a request
for any path starting with `/api`, it forwards it server-side to
`http://localhost:5189` and relays the response back — the browser
never directly talks to port 5189 at all. This is analogous to
`ReverseProxy` / YARP config, or a `launchSettings.json` profile with an
`applicationUrl` plus a reverse-proxy front door — except scoped
specifically to local dev convenience, not a production topology.

`"secure": false` tells the proxy not to validate the target's TLS cert
(irrelevant here since the target is plain `http://`); `"changeOrigin":
true` rewrites the `Host` header on the forwarded request to match the
target, which some backends require.

`angular.json` also sets `"ssl": true` with cert/key paths under
`serve.options` — that's what makes `ng serve` itself serve HTTPS on
4200, using the exported ASP.NET dev cert (see the root `CLAUDE.md` for
the `dotnet dev-certs https --export-path` step). Combined effect:
browser talks HTTPS to 4200, dev server proxies same-origin `/api/*`
calls in plaintext to the API on 5189, API never needs to know about
CORS or HTTPS for local dev.

`environment.development.ts` exists as the dev-specific override
(`angular.json`'s `development` build configuration swaps it in via
`fileReplacements`) — in this repo it's currently identical to
`environment.ts`, but it's the seat where you'd put, e.g., a different
`apiUrl` for a non-proxied dev setup, the same role
`appsettings.Development.json` plays for the API.

---

## 9. Testing — Vitest + `TestBed`

`ng test` runs Vitest (per root `CLAUDE.md`). `task.service.spec.ts`
tests `TaskService` in isolation:

```ts
beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), TaskService],
  });
  service = TestBed.inject(TaskService);
  httpMock = TestBed.inject(HttpTestingController);
});
```

`TestBed` builds a miniature DI container for the test, the same role
`WebApplicationFactory<T>` plays for an ASP.NET integration test, or
just manually `new`-ing up a service with mocked dependencies for a
unit test. `provideHttpClientTesting()` swaps in a fake `HttpBackend` so
no real network call happens; `HttpTestingController` lets the test
assert on what request *would* have been sent and manually supply the
response:

```ts
it('getAll() sends a GET to /api/tasks and returns the list', () => {
  let result: Task[] | undefined;
  service.getAll().subscribe((tasks) => (result = tasks));

  const req = httpMock.expectOne('/api/tasks');
  expect(req.request.method).toBe('GET');
  req.flush([sampleTask]);        // <- fake server response

  expect(result).toEqual([sampleTask]);
});
```

`req.flush(...)` is the moment the fake backend "responds," which is
what makes the earlier `.subscribe()` callback actually fire — a
concrete illustration of §7's laziness point: the request object exists
after `expectOne`, but the `next` callback doesn't run until something
supplies a response, exactly like nothing runs until `.subscribe()` is
called in the first place.

---

## Summary: one full request, start to finish

Tying every section together — what happens when the page loads:

1. Browser loads `https://localhost:4200`, TLS terminated by the
   exported dev cert (`angular.json` `serve.options`, §8).
2. `main.ts` runs `bootstrapApplication(App, appConfig)` (§1), which
   sets up the DI container (`provideHttpClient()`, `provideRouter()`)
   and renders `App`.
3. `App`'s template renders `<app-task-list>` (§2), instantiating
   `TaskList`.
4. Angular constructs `TaskList`; `inject(TaskService)` resolves the
   singleton `TaskService`, which itself resolved `HttpClient` the same
   way (§3).
5. `ngOnInit()` fires, calling `load()` (§7).
6. `load()` calls `taskService.getAll()`, building (but not yet sending)
   an `Observable<Task[]>`. `.subscribe()` sends it: an actual GET to
   `/api/tasks`.
7. The dev server's proxy (§8) intercepts `/api/tasks` and forwards it
   to `http://localhost:5189/api/tasks` — the real .NET API.
8. `TasksController.GetAll()` queries the DB via EF Core, maps
   `TaskItem` entities to `TaskDto`s, returns JSON.
9. The proxy relays the response back to the browser. `HttpClient`
   deserializes the JSON, asserting (not validating — §6) it matches
   `Task[]`.
10. The `next` callback fires: `this.tasks.set(tasks)` (§5).
11. Because the template reads `tasks()` inside `@for` (§4), Angular
    re-renders the list automatically — no manual DOM manipulation
    anywhere in this codebase.

---

## Appendix: `ngModel`, in more depth

§4 introduces `[(ngModel)]` as two-way binding; this expands on what
it actually is, since it's easy to use without understanding it.

`ngModel` is a **directive** (not a component), shipped in
`FormsModule`, that attaches to a native form element (`<input>`,
`<select>`, `<textarea>`) and does two things at once:

1. **Reads** the bound property into the control's value (`newTitle` →
   the input's displayed text).
2. **Writes** back on every user keystroke/change (typing → `newTitle`
   gets updated).

`[(ngModel)]="newTitle"` — the "banana in a box" syntax — is literally
sugar for writing both bindings separately:

```html
[ngModel]="newTitle" (ngModelChange)="newTitle = $event"
```

**Why it needs `FormsModule` imported:** unlike `{{ }}` or `[ ]`/`( )`
binding syntax, which are built into the template compiler, `ngModel`
is a directive that has to be imported before the compiler recognizes
the attribute at all. Skip the import and `[(ngModel)]` in a template
is a compile error — the same way referencing a Tag Helper without its
assembly reference fails.

Two more things worth knowing:

- `ngModel` is the **template-driven forms** approach. Angular's other
  approach, **reactive forms**, builds the form model explicitly in
  TypeScript (`FormGroup`/`FormControl`) instead of inferring it from
  the template. This repo uses template-driven (`ngModel`) since the
  add-task form is simple — reactive forms tend to be preferred once
  you need validation logic, dynamic fields, or unit-testable form
  state without a rendered DOM.
- Inside a `<form>` (like `(ngSubmit)="addTask()"` in this repo), every
  `ngModel`-bound input needs a `name` attribute
  (`<input name="title" ... [(ngModel)]="newTitle" />`) — Angular's
  template-driven forms track controls by name to build up an implicit
  form model behind the scenes, and it throws a runtime error without
  one.

The closest ASP.NET-world analogy isn't Razor/Tag Helpers (those are
request/response, not live) — it's closer to WPF/XAML's `{Binding
Path=..., Mode=TwoWay}`: a control and a backing field kept
continuously in sync while the "page" is alive, rather than reconciled
only on postback.

---

## Appendix: anatomy of an `@Injectable` class declaration

§7 shows `TaskService` "in full." Worth taking apart the first two
lines specifically, since more is happening there than it looks like
coming from C#:

```ts
@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly http = inject(HttpClient);
  ...
}
```

**`export`** — plain TS/JS module scoping. By default a class defined
in a file is only visible within that file; `export` makes it
importable elsewhere (`import { TaskService } from './task.service'`).
There's no `public`/`internal`/`private` spectrum like C# assembly
visibility — it's binary: exported (importable) or not. The closest
analogy is a `public class` in C#, except the boundary is "this file,"
not "this assembly."

**`@Injectable({ providedIn: 'root' })`** — this is a TypeScript
**decorator**, a genuinely different mechanism from a C# attribute,
not just similar-looking syntax. A C# attribute is inert metadata —
something has to explicitly read it via reflection later. A TS
decorator is a function that runs **at class-definition time** (when
the module first loads) and is handed the class itself; it can attach
metadata to it or replace it outright. For Angular specifically, its
compiler (Ivy) statically processes `@Injectable`/`@Component` at
*build* time and bakes the DI/component metadata directly into
compiled output — so at runtime there's no reflection happening at
all, unlike pre-Ivy Angular (and unlike C#'s attribute + reflection
pattern, which is inherently runtime-based unless you're doing source
generators).

**The class body — no constructor, and why that's not a bug:**
coming from C#, you'd expect DI to require a constructor parameter —
`public TaskService(HttpClient http)`. This class has none. That works
because in JS/TS, **field initializers run as the first statements of
the constructor**, whether or not you wrote one explicitly — the
compiler generates an implicit constructor whose body is just "run all
the field initializers in order." So `private readonly http =
inject(HttpClient);` executes during construction, exactly when
Angular has an "active injection context" set up for building this
instance. `inject()` reads from that ambient context — it's less like
a normal function call and more like reading a thread-local that
Angular sets right before calling `new TaskService()` and clears right
after.

That's also precisely why `inject()` throws if called anywhere else —
a method invoked later, a `setTimeout` callback, an event handler — the
ambient "currently constructing" context isn't active anymore by the
time that code runs, so there's nothing for it to read from. It only
works inside that narrow construction window (field initializers
count; so does the top of an actual constructor if one is written).

One more thing you'll see constantly in Angular tutorials/StackOverflow
answers: the *older*, still fully valid style is ordinary
constructor-parameter injection —

```ts
constructor(private http: HttpClient) {}
```

— Angular's direct equivalent of C# constructor injection, no
ambient-context magic involved. `inject()` is the newer,
field-initializer-friendly alternative this repo chose; they're
functionally interchangeable, just different syntax for the same DI
resolution.

---

## Appendix: `toggleDone`, line by line

§7 calls out `toggleDone` as an optimistic-update pattern; this walks
through why each line is written the way it is.

```ts
toggleDone(task: Task): void {
  const updated = { ...task, isDone: !task.isDone };
  this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? updated : t)));

  this.taskService
    .update(task.id, { title: updated.title, description: updated.description,
                        isDone: updated.isDone, dueDate: updated.dueDate })
    .subscribe({
      error: () => {
        this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? task : t)));
        this.error.set('Could not update the task. Please try again.');
      },
    });
}
```

**`toggleDone(task: Task): void`** — takes the specific `Task` object
for the row that was clicked (bound in the template per §4's event
binding) and returns nothing; the method manages its own subscription
internally rather than returning an `Observable` for the caller to
deal with.

**`const updated = { ...task, isDone: !task.isDone };`** — the spread
creates a **new object**, a shallow copy of `task` with `isDone`
flipped, without mutating `task` itself. The closest C# equivalent is
a record `with` expression (`task with { IsDone = !task.IsDone }`) —
same immutable-update intent. That intent matters more here than it
would in C#, for the reason in the next line.

**`this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? updated : t)));`**
— the optimistic UI flip, and the reason the previous line had to
build a new object instead of mutating in place. `tasks.map(...)`
builds a **brand-new array**, swapping in `updated` for the matching
element and passing every other element through unchanged.
`signal.update(fn)` calls `fn` with the signal's current value and
stores whatever it returns.

The non-obvious part: Angular signals detect a change via reference
equality (`Object.is`) by default. If this had instead mutated an
element in place — `tasks()[i].isDone = true` — without producing a
new array, the signal's stored reference would never change,
`Object.is` would report no change, and **nothing would re-render**,
even though the underlying data technically changed. The object spread
and the `.map()` (which always returns a fresh array) aren't style
preferences here — they're the mechanism that makes the signal notice
anything happened at all.

**`this.taskService.update(task.id, {...}).subscribe({...})`** —
recall §7's laziness point: `.update()` just *builds* an
`Observable<void>` describing a PUT request; nothing is sent until
`.subscribe()` runs. So the real order of events is: (1) flip the
object, (2) show the flipped state in the UI immediately via the
signal update above, (3) *then* actually fire the PUT request. The UI
has already changed before the network call has even been dispatched.

**The object literal passed to `.update()`** — `{ title: ...,
description: ..., isDone: ..., dueDate: ... }` — matches the
`UpdateTask` shape from §6 (no `id`/`createdAt`). `updated` itself is a
full `Task` (it has `id` and `createdAt` too, spread from `task`), and
TypeScript's structural typing would actually allow passing `updated`
directly here without complaint — it has every field `UpdateTask`
needs, plus extras. The author rebuilt it explicitly anyway, mirroring
the same discipline the root `CLAUDE.md` enforces server-side
(controllers return DTOs, never raw entities): make the wire payload's
shape explicit at the call site rather than relying on incidental
structural compatibility to "just happen to work."

**`.subscribe({ error: () => {...} })`** — only an `error` callback, no
`next`. On success there's nothing left to do — the optimistic update
already shows the correct end state, so a `next` handler would be
empty anyway. Only failure needs a reaction.

**Inside the error handler**, two things happen:
- `this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? task : t)));`
  — the rollback. Same map-by-id-and-replace pattern as the optimistic
  update, but swapping the **original** `task` back in (not
  `updated`). This only works because `task` is captured by closure
  from the method's parameter — even though this callback runs later,
  asynchronously, after the PUT round-trip fails, it still refers to
  the exact original object from when `toggleDone` was called, not
  "whatever's currently in the signal."
- `this.error.set('Could not update the task. Please try again.');` —
  writes the same error-banner signal §4/§5 use for the initial load,
  so the template can surface the failure.

So the whole method is: optimistically mutate-by-replacement locally →
fire the request → only touch state again if it fails, and when it
does, undo using the pre-toggle object already sitting in the closure.
