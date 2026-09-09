# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A to-do list app in two halves: `src/TodoApi` (ASP.NET Core minimal APIs on .NET 10, SQL
Server, in the `TodoApi.slnx` solution) and `src/todo-web` (Angular 22). Items carry a
**status** and a **priority**, and `GET /api/todos` filters, sorts and pages on both.

Both halves must run for the UI to work: the API on 5062, the Angular dev server on 4200
proxying `/api` to it.

## Commands

```bash
dotnet build                                        # whole solution
dotnet run --project src/TodoApi                    # http://localhost:5062
dotnet run --project src/TodoApi --launch-profile https   # adds https://localhost:7142

dotnet ef migrations add <Name> --project src/TodoApi --output-dir Data/Migrations
dotnet ef database update --project src/TodoApi
dotnet ef database drop --project src/TodoApi       # then `update` to rebuild from scratch

npm start --prefix src/todo-web                     # dev server on 4200, proxies /api
npm run build --prefix src/todo-web
npx ng test --watch=false                           # from src/todo-web; `npm test` watches
npx ng test --watch=false --filter "renders a row"  # one test, by name regex
```

`dotnet ef` is a global tool (`dotnet tool install --global dotnet-ef --version 10.0.12`).
Keep the `--output-dir` argument on `migrations add`; without it EF drops migrations in a
top-level `Migrations/` folder instead of alongside the context in `Data/`.

The API has no test project yet. If one is added, the conventions are `dotnet test`, and
`dotnet test --filter "FullyQualifiedName~SomeTest"` for a single test. The front end has
vitest specs behind the Angular unit-test builder — narrow a run with its own `--filter`
(a regex over suite and test names), **not** a `--` passthrough to vitest, which the builder
rejects. `--watch` defaults to true in a terminal and false otherwise.

The API holds a lock on `bin/Debug/net10.0/TodoApi.exe` while running, so `dotnet build`
fails with MSB3027 until you stop it.

Manual verification: **Swagger UI at `/swagger`** (Development only) with try-it-out already
enabled, `src/TodoApi/TodoApi.http` for the same requests as a file, or plain
`curl -s http://localhost:5062/api/todos`. The OpenAPI document is at `/openapi/v1.json` and
`/health` is always mapped.

The document comes from `Microsoft.AspNetCore.OpenApi`; `Swashbuckle.AspNetCore.SwaggerUI`
contributes only the page and is pointed at that document. Do not add Swashbuckle's own
generator (`AddSwaggerGen`) - two generators would drift apart.

## SDK pin — do not remove

`global.json` pins the SDK to **10.0.302**. This machine also has an 11.0 preview SDK that
`dotnet` selects by default, so without the pin the project silently builds against the
preview. Keep `<TargetFramework>net10.0</TargetFramework>` and the pin in step.

Package versions in `TodoApi.csproj` are pinned deliberately: `Microsoft.OpenApi` is
referenced explicitly (rather than left transitive) because the version
`Microsoft.AspNetCore.OpenApi` pulls in by default carries an advisory (NU1903). Builds are
expected to be warning-free — if a package bump reintroduces a warning, fix it rather than
tolerating it.

## Architecture

Request flow: `Program.cs` (host, DI, JSON, pipeline) → `Endpoints/TodoEndpoints.cs`
(a `/api/todos` route group; every handler is a static method returning `TypedResults`) →
`TodoDbContext` → EF Core. `Contracts/` holds the request/response records; the entity in
`Models/TodoItem.cs` is never returned directly — handlers map through
`TodoResponse.FromEntity`, which computes `isOverdue`.

Development-only behaviour is grouped in one `if (app.Environment.IsDevelopment())` block:
`MapOpenApi()`, `UseSwaggerUI()`, `MigrateAsync()` and seeding. Outside Development both
`/openapi/v1.json` and `/swagger` are 404s, and `GET /` redirects to `/health` instead of
`/swagger`. `/health` is always mapped.

Two different things name that page: the heading inside it comes from the `OpenApiInfo` set
by the document transformer on `AddOpenApi`, while the browser tab title is
`options.DocumentTitle` on `UseSwaggerUI`. Change both together.

## Database

**SQL Server**, database `TodoDb` on the local default instance via Windows auth. The
connection string is `ConnectionStrings:TodoDb` in `appsettings.json`; override it with user
secrets or `ConnectionStrings__TodoDb` rather than editing the checked-in file. A missing
string throws at startup with a named message instead of failing on the first query.

`Program.cs` holds the only provider-specific line (`UseSqlServer`), so swapping providers is
that call plus regenerated migrations.

In Development *only*, startup runs `MigrateAsync()` and then `TodoSeeder`, which inserts four
sample items if `Todos` is empty. Both are deliberately Development-gated — do not extend
auto-migration to other environments.

Seed timestamps are relative to the moment of first seeding (`UtcNow.AddDays(...)`), and the
data now persists, so they drift from "today" as time passes. To get fresh sample data,
`dotnet ef database drop` then `database update` and restart.

`EnableRetryOnFailure` is on, which means the retrying execution strategy is active. If you
ever add an explicit `BeginTransaction`, it will throw — wrap that work in
`db.Database.CreateExecutionStrategy().ExecuteAsync(...)` instead.

All list filtering, sorting and paging must stay server-side: the endpoint composes one
`IQueryable` that EF translates to a single `SELECT ... ORDER BY ... OFFSET/FETCH`. This is
why the search filter uses `ToLower().Contains(...)` rather than
`StringComparison.OrdinalIgnoreCase` — the latter does not translate and would silently
pull the whole table into memory.

## Front end (`src/todo-web`)

Angular 22: standalone components, **zoneless** change detection, signals for all state. No
NgModules; RxJS appears only as the `firstValueFrom` wrapper around mutation calls.

`TodoStore` is the entire state layer. A `filters` signal feeds an `httpResource`, so a
filter change refetches by itself — never call a "load" method. Mutations go through
`HttpClient` and then `page.reload()`. `toParams` deliberately omits empty filters, because
the API treats an absent parameter as "no filter" and an empty one as a value.

`TodoList` is the only component with markup. Its inputs are bound with explicit
`[value]`/`(input)` pairs rather than `ngModel`, which keeps `FormsModule` out and matches
the signal-based state.

Two things that will bite in tests: `fixture.whenStable()` does **not** settle while an
`httpResource` is in play — use the `settle()` helper in `todo-list.spec.ts`
(`await new Promise(r => setTimeout(r, 0))` then `fixture.detectChanges()`). And every spec
needs `provideZonelessChangeDetection()` in its providers.

Component styles are budgeted at 4 kB (warning) / 8 kB (error) per file in production
builds, which is why the design tokens and shared element styles live in the global
`src/styles.css` rather than in the component.

## Two-origin setup

`proxy.conf.json` sends `/api` from the dev server to `http://localhost:5062`, so the SPA
uses relative URLs and never triggers CORS. The API additionally has a named CORS policy
reading `Cors:AllowedOrigins` (set only in `appsettings.Development.json`) for the case where
the SPA is served from its own origin — it grants nothing when that list is absent, which is
the intended production default.

## Conventions that carry decisions

- **Enums are names in JSON, ints in storage.** `JsonStringEnumConverter` is configured
  globally, and `TodoDbContext` maps `Status`/`Priority` with `HasConversion<int>()`. The int
  storage is what makes `sortBy=Priority` order Low → Critical instead of alphabetically —
  do not switch these to string columns.
- **`[AsParameters]` binding requires nullable properties.** In `TodoQuery`, `SortBy`,
  `Descending`, `Page` and `PageSize` are nullable with defaults applied inside the handler.
  Making any of them a non-nullable value type turns it into a *required* query parameter and
  a bare `GET /api/todos` starts throwing.
- **`CompletedAt` is derived from status**, not client-supplied: `ApplyStatus` stamps it on
  the first move to `Completed` and clears it when the item is re-opened. Any new path that
  changes status must go through that helper.
- **Errors are RFC 9457 problem details throughout.** Validation returns 400 with an `errors`
  dictionary keyed by lowercase field name; missing ids return 404 via `NotFoundProblem`.
  `Infrastructure/BadRequestExceptionHandler.cs` exists so an unreadable body — malformed
  JSON, or an unknown enum name like `"priority": "Urgent"` — surfaces as 400 rather than 500.
- **List sorting always breaks ties by `Id`** so paging is stable, and items with no due date
  sort last under `sortBy=DueDate` regardless of direction.
- **Ids come from the app, not the database.** `TodoItem.Id` is initialised with
  `Guid.CreateVersion7()` and the column has no `NEWSEQUENTIALID()` default. Version 7 GUIDs
  are time-ordered, which keeps inserts from fragmenting the clustered primary key — do not
  swap in `Guid.NewGuid()`.
- **`BadRequestExceptionHandler` returns `false` for anything it does not recognise**, so
  genuine faults still log and return 500. It only claims `JsonException` and
  `BadHttpRequestException` (including those nested as inner exceptions).
- **Optional request fields carry C# default values** (`TodoStatus? Status = null`), which is
  what keeps them out of the OpenAPI `required` list. `UpdateTodoRequest` lists its
  positional parameters in a different order from the JSON for that reason - optional
  parameters must come last. JSON binds by name, so the wire contract is unaffected.
- Handlers declare their full result union (e.g.
  `Results<Ok<TodoResponse>, NotFound<ProblemDetails>, ValidationProblem>`) so the OpenAPI
  document stays accurate without explicit `Produces` calls.
