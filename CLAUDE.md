# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A to-do list app in two halves: `src/TodoApi` (ASP.NET Core minimal APIs on .NET 10, SQL
Server, in the `TodoApi.slnx` solution) and `src/todo-web` (Angular 22). Items carry a
**status** and a **priority**, and `GET /api/todos` filters, sorts and pages on both. The API
also hosts a multi-agent assistant (Microsoft Agent Framework against Azure AI Foundry) that
the front end shows in the right third of the home screen.

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

The assistant needs Foundry credentials, which live in user secrets and never in the repo:

```bash
cd src/TodoApi
dotnet user-secrets set "Ai:Endpoint" "https://<resource>.services.ai.azure.com/"
dotnet user-secrets set "Ai:ApiKey"   "<key>"
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
`/health` is always mapped. `curl -s http://localhost:5062/api/chat/status` reports whether
the assistant is configured and which deployment it will use - check it first when chat
misbehaves, before reading any agent code.

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

Request flow: `Program.cs` (host, DI, JSON, pipeline) → `Endpoints/` (`TodoEndpoints` for
`/api/todos`, `ChatEndpoints` for `/api/chat`; every handler is a static method returning
`TypedResults`) → `Services/TodoService.cs` → `TodoDbContext` → EF Core.

**`TodoService` is the only thing that touches `TodoDbContext`.** The endpoints hold no query
or persistence logic at all - they map a service result to an HTTP result and nothing more,
so a handler that injects `TodoDbContext` is a regression. The agent tools go through the
same service, which is why chat and REST cannot drift.

`Contracts/` holds the request/response records; the entity in `Models/TodoItem.cs` is never
returned directly — everything maps through `TodoResponse.FromEntity`, which computes
`isOverdue`.

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

All list filtering, sorting and paging must stay server-side: `TodoService.ListAsync`
composes one `IQueryable` (via its `Filter` and `ApplySort` helpers) that EF translates to a
single `SELECT ... ORDER BY ... OFFSET/FETCH`. This is why the search filter and
`TodoTools.ListTodos` share it, and why the search uses `ToLower().Contains(...)` rather than
`StringComparison.OrdinalIgnoreCase` — the latter does not translate and would silently
pull the whole table into memory.

## Front end (`src/todo-web`)

Angular 22: standalone components, **zoneless** change detection, signals for all state. No
NgModules; RxJS appears only as the `firstValueFrom` wrapper around mutation calls.

Two root-provided stores hold all state; components hold only view state such as an open
composer or an unsent draft.

`TodoStore` owns the list. A `filters` signal feeds an `httpResource`, so a filter change
refetches by itself — never call a "load" method. Mutations go through `HttpClient` and then
`page.reload()`. `toParams` deliberately omits empty filters, because the API treats an
absent parameter as "no filter" and an empty one as a value.

`ChatStore` owns the conversation: the transcript for display, plus the server-issued
`conversationId` that carries history. The server, not the client, remembers the thread.

`TodoList` and `ChatPanel` are the components with markup. Inputs are bound with explicit
`[value]`/`(input)` pairs rather than `ngModel`, which keeps `FormsModule` out and matches
the signal-based state.

**Every `<select>` needs `[selected]` on its options, not just `[value]` on the select.**
With options rendered by `@for`, Angular applies the select's `value` before the options
exist, so the control silently falls back to its first option while the underlying signal is
correct - a bug you see only by looking at the page. `ChatStore` calls `TodoStore.refresh()`
after any turn that reported changes, which is how chat edits reach the list.

Two things that will bite in tests: `fixture.whenStable()` does **not** settle while an
`httpResource` is in play — use the `settle()` helper in `todo-list.spec.ts`
(`await new Promise(r => setTimeout(r, 0))` then `fixture.detectChanges()`). And every spec
needs `provideZonelessChangeDetection()` in its providers.

Component styles are budgeted at 4 kB (warning) / 8 kB (error) per file in production
builds, which is why the design tokens and shared element styles live in the global
`src/styles.css` rather than in the component.

**The palette is a system, not decoration.** `src/styles.css` defines a token pair per
priority and per status (`--high`/`--high-fg` and so on). Every filled badge uses the pair,
because the foregrounds were picked to clear WCAG AA against their own background - amber
takes near-black text, the darker hues take white. Adding a colour means adding both halves
of the pair and checking contrast, in the light block *and* the dark one. The same tokens
drive each row's left stripe and the per-agent chat badges, so a hue means one thing
everywhere.

## Request validation

**FluentValidation**, one mechanism for every body-carrying route — there is no hand-rolled
checking left in the handlers, and none should come back. Rules live in
`Validators/TodoRequestValidators.cs` (one validator per request record, shared limits in
`TodoRules`), are registered by assembly scan in `Program.cs`, and run through the generic
`ValidationFilter<T>` in `Infrastructure/ValidationFilter.cs`.

A route opts in with `.WithValidation<TRequest>()`, which adds the filter *and* calls
`ProducesValidationProblem()` so the 400 shows up in the OpenAPI document and on the Swagger
page. Adding a validated route means adding both the validator and that call.

Consequences worth knowing:

- The filter runs **before** the handler, so an invalid body against a nonexistent id returns
  400, not 404.
- Handlers for validated routes no longer list `ValidationProblem` in their result union;
  the filter owns that response.
- `ValidationFilter` camel-cases FluentValidation's CLR property names (`Title` → `title`)
  so the error keys stay the JSON names the Angular client already reads.
- An unknown enum *name* still fails at deserialization, so it comes back from
  `BadRequestExceptionHandler` as a 400 with `detail` rather than an `errors` map. An
  out-of-range enum *number* binds fine and is caught by `IsInEnum()` instead.
- Create treats `status`/`priority` as optional (`.When(x => x.Status.HasValue)`); a replace
  requires them. Keep that split in step with the `required` list in the OpenAPI schema.

## Two-origin setup

`proxy.conf.json` sends `/api` from the dev server to `http://localhost:5062`, so the SPA
uses relative URLs and never triggers CORS. The API additionally has a named CORS policy
reading `Cors:AllowedOrigins` (set only in `appsettings.Development.json`) for the case where
the SPA is served from its own origin — it grants nothing when that list is absent, which is
the intended production default.

## The assistant (`src/TodoApi/Agents`)

A supervisor team, all in-process: a **Coordinator** with no domain tools routes to
**Scheduler** (writes), **Analyst** (reads) and **Guide** (RAG over `Knowledge/*.md`). The
specialists are handed to the coordinator as ordinary `AIFunction`s by the local `Delegate`
helper, which also records which ones ran so the UI can badge them. Keep the write tools on
the Scheduler only - that separation is the reason the coordinator cannot delete an item
while answering a question.

**Every tool goes through `TodoService`.** That is the whole point of the service existing:
chat and REST share one implementation, so they cannot drift on defaults, trimming or
`CompletedAt`. Never give an agent its own `TodoDbContext`.

The team is registered **scoped** and rebuilt per request, because the tools write through a
scoped `TodoService`. Constructing agents is cheap; only the model call is not.

`IChatClient` is wrapped in `.UseFunctionInvocation()` so tool calls round-trip automatically.

`Knowledge/**/*.md` is copied to the output directory by a `Content` item in
`TodoApi.csproj`, and `KnowledgeBase` reads from `ContentRootPath/Knowledge` at runtime. New
files are picked up by the glob, but they are only embedded on the *next* start - the index
is built once per process.

RAG is deliberately dependency-free: passages are split one per `##` heading, embedded with
`text-embedding-3-small` on first use, and ranked by cosine similarity in memory. The corpus
is a few kilobytes. If it grows past what fits comfortably in memory, replace
`KnowledgeBase.SearchAsync` with Azure AI Search rather than sharding this.

Configuration lives in user secrets (`Ai:Endpoint`, `Ai:ApiKey`), never `appsettings.json`.
When they are absent the AI services are simply not registered, the REST API is unaffected,
and `/api/chat` returns 503 while `/api/chat/status` explains why - keep that graceful path
working, since it is what a fresh clone hits.

Conversation history is in-memory in `ConversationStore`, capped and expiring. It is not
durable on purpose.

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
  dictionary keyed by the JSON field name; missing ids return 404 via `NotFoundProblem`.
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
  `Results<Ok<TodoResponse>, NotFound<ProblemDetails>>`) so the OpenAPI document stays
  accurate without explicit `Produces` calls. The 400 is *not* in these unions: it comes from
  the validation filter, which documents itself.
