Todo app
Built an ASP.NET Core Minimal API on .NET 10  "Todo app" with Database in SQL Server localhost instance, Angular 22 UI, and an Azure Foundry multi-agent chat panel; all of it works.
Todo items carry a status and priority and the list filters, sorts and pages on both
=======
Frontend :
  <img width="1637" height="992" alt="image" src="https://github.com/user-attachments/assets/edbb6bbd-f5c9-4e46-a699-43f1a0b5ca1b" />
  <img width="1565" height="990" alt="image" src="https://github.com/user-attachments/assets/37f6038f-9e3b-4cc6-b8b9-caff13af3834" />

Backend :
  <img width="3745" height="1870" alt="image" src="https://github.com/user-attachments/assets/271eff50-d1f9-477d-b445-0730b7ab8964" />
  <img width="780" height="310" alt="image" src="https://github.com/user-attachments/assets/c5ef787c-019a-4b7d-8a54-3f7c9b4c807c" />

Sql Server :
  <img width="3795" height="1602" alt="image" src="https://github.com/user-attachments/assets/4d0bb279-021e-4f16-835b-f5b797e80828" />
>>>>>>> 798b30c380c99b8d902aa1d3fb00b6b552ad0b25

The home screen is the list on the left two thirds and the assistant on the right third.
Anything you can do by clicking, you can also ask for in plain English.

The SDK is pinned to 10.0.302 in `global.json`, so the machine's newer .NET 11 preview SDK
is ignored for this solution.

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
- API: <http://localhost:5062>
- OpenAPI document: <http://localhost:5062/openapi/v1.json>
- Health: <http://localhost:5062/health>

`src/TodoApi/TodoApi.http` has a ready-made request for every endpoint (VS Code REST Client,
Visual Studio, or JetBrains Rider).

## The assistant

A supervisor team of four agents, all in-process in the API (`src/TodoApi/Agents`):

| Agent | Role | Tools |
| ----- | ---- | ----- |
| **Coordinator** | Routes each turn; holds no domain tools of its own | the three specialists |
| **Scheduler** | Anything that *changes* items | create, set status, set priority, delete, list |
| **Analyst** | Read-only questions, counts, what is overdue | list, statistics |
| **Guide** | How the app and API work | retrieval over `Knowledge/*.md` |

The specialists are exposed to the coordinator as ordinary function tools, so routing is a
normal tool call and the coordinator cannot, say, delete an item while "just answering a
question". The reply shows which specialists ran.

Every tool goes through the same `TodoService` the REST endpoints use, so an item created by
chat is indistinguishable from one created by `POST /api/todos` - same defaults, same
validation, same `completedAt` handling. When a turn changes anything the list refreshes
itself; there is no polling.

**RAG.** The Guide answers only from `src/TodoApi/Knowledge/*.md`, split one passage per
`##` heading, embedded with `text-embedding-3-small` on first use and searched by cosine
similarity in memory. The corpus is a few kilobytes, so this needs no vector database and no
extra Azure resource. Edit the Markdown and restart to change what the Guide knows.

### Configuring it

The assistant needs an Azure AI Foundry endpoint and key. Keep them out of the repo:

```bash
cd src/TodoApi
dotnet user-secrets set "Ai:Endpoint" "https://<your-resource>.services.ai.azure.com/"
dotnet user-secrets set "Ai:ApiKey"   "<key>"
```

`Ai:ChatDeployment` defaults to `gpt-5-mini` and `Ai:EmbeddingDeployment` to
`text-embedding-3-small`; override either in `appsettings.json` if your deployment names
differ. Without these the API runs normally and only the chat panel reports itself
unavailable - `GET /api/chat/status` says so, and the panel shows the reason.

`POST /api/chat` takes `{ "message": "...", "conversationId": "..." }` and returns the reply,
which agents ran, and the ids of anything changed. History is kept in memory per conversation
and expires after two hours.

## Front end

Angular 22, standalone components, zoneless change detection, signals throughout - no
NgModules and no RxJS beyond the one `firstValueFrom` used for mutations. The palette is
deliberately bright: a colour per priority and per status, carried consistently onto the
badges, each row's left stripe, and the agent labels in chat. Every filled badge pairs its
background with a foreground that clears WCAG AA, and the whole thing has a dark theme.

`TodoStore` (`src/app/services/todo-store.ts`) is the whole state layer: a `filters` signal
feeds an `httpResource`, so changing a filter refetches on its own, and mutations post
through `HttpClient` and then `reload()` that resource. `TodoList` is the only component with
markup - filter bar, inline composer, per-row status/priority selects, inline edit, and paging.

```bash
npm start   --prefix src/todo-web    # dev server with the API proxy
npm run build --prefix src/todo-web  # production bundle into dist/
npm test    --prefix src/todo-web    # vitest, single run: npx ng test --watch=false
```

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
  Agents/                   Agent team, tools, RAG index and AI options
  Knowledge/                Markdown corpus the Guide agent retrieves from
  Services/                 TodoService - the one implementation of every operation
src/todo-web/               Angular 22 front end
  proxy.conf.json           Dev-server proxy for /api
  src/app/models/           Types mirroring the API contract
  src/app/services/         TodoStore and ChatStore
  src/app/todos/            The list component, template and styles
  src/app/chat/             The assistant panel
```
