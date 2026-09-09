# Todo app
Frontend :
  <img width="2840" height="2012" alt="image" src="https://github.com/user-attachments/assets/8211141d-5f44-4fd2-9a5c-e7baf1ce91a9" />

Backend :
  <img width="3745" height="1870" alt="image" src="https://github.com/user-attachments/assets/271eff50-d1f9-477d-b445-0730b7ab8964" />
  <img width="780" height="310" alt="image" src="https://github.com/user-attachments/assets/c5ef787c-019a-4b7d-8a54-3f7c9b4c807c" />

Sql Server :
  <img width="3795" height="1602" alt="image" src="https://github.com/user-attachments/assets/4d0bb279-021e-4f16-835b-f5b797e80828" />

A to-do list with an **ASP.NET Core minimal API on .NET 10** backed by SQL Server, and an
**Angular 22** front end. Items carry a **status** and a **priority**, and the list filters,
sorts and pages on both.

The SDK is pinned to 10.0.302 in `global.json`, so a newer .NET 11 preview SDK on the same
machine is ignored for this solution.

## Prerequisites

- **.NET SDK 10.0.302** (or a later 10.0.3xx feature band - `global.json` rolls forward
  within the band). Check with `dotnet --list-sdks`.
- **Node.js** `^22.22.3 || ^24.15.0 || >=26.0.0`, as required by Angular 22. Check with
  `node --version`.
- **SQL Server** reachable at `localhost` as a default instance, with Windows authentication
  for the account running the API. Built against SQL Server 2025 Developer/Enterprise
  Evaluation; any recent edition, including Express, works.
- **`dotnet-ef`**, once per machine:
  `dotnet tool install --global dotnet-ef --version 10.0.12`

## First run

```bash
dotnet ef database update --project src/TodoApi
```

That creates the `TodoDb` database if it does not exist and applies the migrations - there is
no manual `CREATE DATABASE` step. In Development the app does the same thing on startup, so
you can skip straight to running it; the explicit command is useful when you want the schema
in place first, or when running outside Development where startup migration is disabled.

## Run it

Two terminals - the API first, then the front end:

```bash
dotnet run --project src/TodoApi          # http://localhost:5062
npm start --prefix src/todo-web           # http://localhost:4200
```

Open <http://localhost:4200>. The Angular dev server proxies `/api` to the API
(`src/todo-web/proxy.conf.json`), so the browser only ever talks to one origin and CORS
does not come into it.

- UI: <http://localhost:4200>
- **Swagger UI: <http://localhost:5062/swagger>** - browse and call every endpoint
- API: <http://localhost:5062> (redirects to Swagger UI in Development, `/health` elsewhere)
- OpenAPI document: <http://localhost:5062/openapi/v1.json>
- Health: <http://localhost:5062/health>

## Testing the API in the browser

<http://localhost:5062/swagger> lists all eight operations. Expand one, edit the request and
press **Execute** - "Try it out" is already enabled, so there is no extra click per
operation, and each response shows its duration.

Status and priority render as dropdowns of the real enum names, and only genuinely required
fields are marked required (`title` for a create; `title`, `status` and `priority` for a
full replace), so a minimal `{ "title": "..." }` create works straight from the page.

Swagger UI is Development-only. The document itself is produced by
`Microsoft.AspNetCore.OpenApi`; `Swashbuckle.AspNetCore.SwaggerUI` supplies only the page,
so there is no second document generator in the app.

`src/TodoApi/TodoApi.http` has the same requests as a file, for VS Code REST Client, Visual
Studio, or JetBrains Rider.

## Front end

Angular 22, standalone components, zoneless change detection, signals throughout - no
NgModules and no RxJS beyond the one `firstValueFrom` used for mutations.

`TodoStore` (`src/app/services/todo-store.ts`) is the whole state layer: a `filters` signal
feeds an `httpResource`, so changing a filter refetches on its own, and mutations post
through `HttpClient` and then `reload()` that resource. `TodoList` is the only component with
markup - filter bar, inline composer, per-row status/priority selects, inline edit, and paging.

```bash
npm start     --prefix src/todo-web   # dev server with the API proxy
npm run build --prefix src/todo-web   # production bundle into dist/
npm test      --prefix src/todo-web   # vitest; watches in a terminal
```

From `src/todo-web`, `npx ng test --watch=false` runs the suite once and
`npx ng test --watch=false --filter "renders a row"` runs a single test by name.

If you serve the SPA from its own origin instead of using the proxy, the API allows the
origins listed under `Cors:AllowedOrigins` in `appsettings.Development.json`
(`http://localhost:4200` out of the box).

## Database

Storage is **SQL Server** via EF Core, database `TodoDb` on the local default instance
(`Server=localhost`, Windows authentication). The connection string lives under
`ConnectionStrings:TodoDb` in `src/TodoApi/appsettings.json`; point it elsewhere with user
secrets or the `ConnectionStrings__TodoDb` environment variable rather than editing the file.

The schema comes from EF Core migrations in `src/TodoApi/Data/Migrations`:

```bash
dotnet ef migrations add <Name> --project src/TodoApi --output-dir Data/Migrations
dotnet ef database update --project src/TodoApi
dotnet ef migrations list --project src/TodoApi
```

`dotnet ef` needs the tool installed once: `dotnet tool install --global dotnet-ef --version 10.0.12`.

In Development the app applies pending migrations on startup and seeds four sample items if
`Todos` is empty; both are skipped in other environments, where migrations should be applied
as a deliberate step. To start over: `dotnet ef database drop --project src/TodoApi` then
`dotnet ef database update --project src/TodoApi`.

## Model

| Field         | Type              | Notes                                              |
| ------------- | ----------------- | -------------------------------------------------- |
| `id`          | GUID (v7)         | Server-assigned, time-ordered                       |
| `title`       | string            | Required, max 200 chars                             |
| `description` | string?           | Max 2000 chars                                      |
| `status`      | `TodoStatus`      | `Pending`, `InProgress`, `Completed`, `Cancelled`   |
| `priority`    | `TodoPriority`    | `Low`, `Medium`, `High`, `Critical`                 |
| `dueDate`     | timestamp?        | Optional                                            |
| `isOverdue`   | bool              | Computed: past due and not completed/cancelled      |
| `createdAt` / `updatedAt` | timestamp | Server-maintained                           |
| `completedAt` | timestamp?        | Stamped on completion, cleared if re-opened         |

Enums are sent and returned as **names**, not numbers. They are stored as their underlying
int, so `sortBy=Priority` orders Low → Critical rather than alphabetically.

Only `title` is required to create an item; `status` defaults to `Pending`, `priority` to
`Medium`, and everything else is optional. A `PUT` is a full replacement, so it requires
`title`, `status` and `priority`.

## Endpoints

| Method   | Route                           | Purpose                                  |
| -------- | ------------------------------- | ---------------------------------------- |
| `GET`    | `/api/todos`                    | List with filtering, sorting, paging     |
| `GET`    | `/api/todos/{id}`               | Fetch one                                |
| `POST`   | `/api/todos`                    | Create (201 + `Location`)                |
| `PUT`    | `/api/todos/{id}`               | Replace                                  |
| `PATCH`  | `/api/todos/{id}/status`        | Change status only                       |
| `PATCH`  | `/api/todos/{id}/priority`      | Change priority only                     |
| `DELETE` | `/api/todos/{id}`               | Delete (204)                             |

### Query parameters for `GET /api/todos`

| Parameter     | Example                | Meaning                                             |
| ------------- | ---------------------- | --------------------------------------------------- |
| `status`      | `status=Pending`       | Exact status match                                  |
| `priority`    | `priority=High`        | Exact priority match                                |
| `minPriority` | `minPriority=High`     | This priority or above                              |
| `search`      | `search=domain`        | Case-insensitive title/description match            |
| `isOverdue`   | `isOverdue=true`       | Only overdue (or, with `false`, only not overdue)   |
| `dueBefore`   | `dueBefore=2026-10-01T00:00:00Z` | Due on or before an instant               |
| `sortBy`      | `sortBy=Priority`      | `CreatedAt` (default), `UpdatedAt`, `DueDate`, `Priority`, `Status`, `Title` |
| `descending`  | `descending=true`      | Reverse the sort                                    |
| `page`        | `page=2`               | 1-based, default 1                                  |
| `pageSize`    | `pageSize=50`          | Default 20, capped at 200                           |

Items with no due date always sort last under `sortBy=DueDate`, and ties break by `id` so
paging stays stable.

Example — the most urgent unfinished work first:

```
GET /api/todos?status=Pending&sortBy=Priority&descending=true
```

Response shape:

```json
{
  "items": [ /* TodoResponse objects */ ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 4,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

## Errors

Every failure returns RFC 9457 problem details:

- `400` — validation failures (`errors` keyed by field), malformed JSON, or an unknown
  status/priority name.
- `404` — no item with that id.

## Layout

```
global.json                 SDK pin (10.0.302)
TodoApi.slnx
src/TodoApi/                ASP.NET Core minimal API
  Program.cs                Host, DI, JSON and pipeline setup
  appsettings.json          Includes the TodoDb connection string
  Models/                   TodoItem, TodoStatus, TodoPriority
  Contracts/                Request/response records and the query object
  Data/                     TodoDbContext, the seeder and Migrations/
  Endpoints/                Route group and handlers
  Infrastructure/           Maps bad request bodies to 400 problem details
  TodoApi.http              Sample requests
src/todo-web/               Angular 22 front end
  proxy.conf.json           Dev-server proxy for /api
  src/app/models/           Types mirroring the API contract
  src/app/services/         TodoStore - filters, httpResource, mutations
  src/app/todos/            The list component, template and styles
```
