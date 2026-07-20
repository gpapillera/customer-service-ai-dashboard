# DIY.md — Build This App Yourself

A from-scratch, beginner-friendly build guide for the **Customer Service AI
Dashboard** — a full-stack demo where an Angular SPA talks to an ASP.NET Core
Web API, which stores data in a database and runs a small machine-learning
model that suggests a case priority (Low / Medium / High) the moment a case is
created.

**What you'll learn:** how a real layered backend (controllers → services →
repositories → EF Core), a standalone-component Angular frontend, JWT auth, and
an offline Python ML pipeline fit together into one working app — and *why*
each piece is shaped the way it is.

**Who this is for:** you know some programming but have never touched this
specific stack (C#/.NET, Angular, or the ML bits). Every step is small and
checkable. Where beginners historically trip, you'll see a ⚠️ note.

**How to read this:** each Part builds one coherent capability and ends with a
"📍 Find it in the code" map and a "Verified working as of" line. The other
docs own different jobs — link to them, don't re-read them here:
- `README.md` — feature list, architecture diagram, project structure.
- `AGENTS.md` — repo conventions and commands for AI agents.
- `docs/MODEL_CARD.md` — details of the ML model.
- `docs/PROGRESS_LOG.md` — the chronological build/fix history (reference only).

---

## Table of Contents

- [Part 0 — Tools & Environment Setup](#part-0--tools--environment-setup)
- Parts 1–N (the build phases) — *outline to be confirmed with you before writing.*

---

## Part 0 — Tools & Environment Setup

Do this **before writing a single line of code**. If any tool is missing or the
wrong version, later steps fail in confusing ways. Verify each one at the end
with the checklist.

### 0.1 .NET SDK 8.0

The backend targets `net8.0` (see `backend/src/CustomerService.Api/CustomerService.Api.csproj`).
You need the **.NET 8 SDK**, not just the runtime.

- **Install (Linux, Debian/Ubuntu):** follow the official instructions at
  <https://dotnet.microsoft.com/download/dotnet/8.0> (the `dotnet-install.sh`
  script or the Microsoft package feed). On macOS/Windows use the official
  installer for ".NET 8 SDK".
- **Why 8 and not latest?** The project's NuGet packages (e.g.
  `Microsoft.AspNetCore.Authentication.JwtBearer` `8.0.8`) are pinned to the
  .NET 8 line. Mixing a newer SDK is usually fine for *building*, but staying on
  8 keeps you aligned with what was actually tested. There is **no `global.json`**
  in this repo, so `dotnet` simply uses whatever 8.x SDK you have installed.

### 0.2 Node.js + npm + Angular CLI

The frontend is Angular **18.2** (`frontend/package.json` pins
`@angular/*` `^18.2.0`, TypeScript `~5.5.2`). Angular 18 supports
**Node.js 18.19+, 20.x, or 22.x**.

- **Install Node:** use the official LTS from <https://nodejs.org> (pick 20.x or
  22.x). Avoid odd-numbered "current" releases for a stable build.
- **npm** ships with Node — no separate install.
- **Angular CLI:** you do **not** need a global install. This guide runs Angular
  through the local binary via `npx ng` (the `frontend/package.json` `scripts`
  already wrap it: `npm start`, `npm run build`, `npm test`). If you prefer a
  global CLI: `npm install -g @angular/cli@18`.

> ⚠️ **Common mistake:** installing the newest Node (e.g. 23/24) "because newer is
> better." Angular 18's supported matrix stops at 22.x. It may still build, but
> you're outside tested territory and error messages get cryptic. Use 20.x or
> 22.x LTS.

### 0.3 Python 3.12 + a virtual environment

The ML pipeline (`ml/`) uses pandas/scikit-learn and exports an ONNX model.
`ml/requirements.txt` recommends **Python 3.12**.

- **Install Python 3.12** via your OS package manager.
- **Create the venv** (required — see warning below):
  ```bash
  python3 -m venv ml/.venv
  source ml/.venv/bin/activate      # Windows: ml\.venv\Scripts\activate
  pip install -r ml/requirements.txt
  ```

> ⚠️ **Common mistake:** running `pip install -r ml/requirements.txt` against the
> **system** Python. On many modern Linux distros the system Python is
> *externally managed* and will refuse, or worse, pollute global packages.
> Always `source ml/.venv/bin/activate` first so `pip` points at the venv. The
> `ml/models/*.onnx` file is git-ignored — you regenerate it by running the
> training script, you never expect it to be committed.

### 0.4 Database — SQLite (zero-config) or SQL Server (alternative)

`backend/src/CustomerService.Api/appsettings.json` sets
`"Database": { "Provider": "SqlServer" }` by default, and `docker-compose.yml`
runs SQL Server 2022 in a container. **But a fresh clone with no extra setup
should run on the SQLite fallback** — you just override the provider:

```bash
# From the backend folder, when you start the API:
Database__Provider=Sqlite dotnet run --project src/CustomerService.Api/CustomerService.Api.csproj
```

SQLite needs **no server, no credentials, no container** — EF Core creates
`customer_service.db` on first run and seeds it. That is the path this guide
assumes for local development.

- **SQL Server (alternative, not required):** if you want the "real" configured
  provider, either run `docker compose up` (spins up SQL Server + API + Nginx at
  `http://localhost:8080`) or install SQL Server locally and set the
  `ConnectionStrings__SqlServer` value. You do **not** need this to follow the
  guide.

> ⚠️ **Common mistake:** cloning the repo, running `dotnet run` with no override,
> and getting a connection error to `localhost,1433`. That's the default
> `SqlServer` provider looking for a database that isn't there. Either pass
> `Database__Provider=Sqlite`, or bring up the `db` service via docker-compose.
> The app uses `EnsureCreated()` + an idempotent seed at startup — there are **no
> EF Core migrations** to apply.

### 0.5 Git + VS Code (recommended)

- **Git:** install from <https://git-scm.com>. Used to clone the repo and (for
  you) to commit your progress.
- **VS Code:** from <https://code.visualstudio.com>. Recommended extensions:
  - **C# Dev Kit** (Microsoft) — IntelliSense, debugging, and solution explorer
    for the `.sln`. Strongly recommended; the backend is a multi-project
    solution (`CustomerServiceApi.sln`).
  - **Angular Language Service** (Angular) — template completions in `.html`.
  - **Python** (Microsoft) — for the `ml/` scripts.

You do **not** need any browser extension or global tooling beyond the above.

### 0.6 Verify your setup

Run each command. If the output matches, you're ready for Part 1.

```bash
dotnet --version
# → 8.0.x  (e.g. 8.0.407)

node --version
# → v20.x or v22.x  (an 18.19+/20/22 LTS line)

npm --version
# → 10.x or 11.x

npx ng version
# → Angular CLI: 18.2.21  (and the local @angular/core 18.2.x)

python3 --version
# → Python 3.12.x

git --version
# → git version 2.x

code --version
# → VS Code 1.9x.x  (only if you installed it)
```

If all six (seven with VS Code) print a sane version, you're set. If `dotnet`
is missing, revisit 0.1; if `ng` complains about Node, revisit 0.2.

---

*Parts 1–N (the actual build phases) follow once we agree on the outline.*

---

## Part 1 — Solution scaffold & layered backend

**What you're building and why:** A .NET solution with five projects that
enforce a clean dependency direction: `Api` (HTTP) → `Application` (business
rules) → `Infrastructure` (EF Core, the database) → `Domain` (entities +
interfaces), plus `ML` (the predictor). The rule of thumb: the controller never
talks to the database directly — it calls an `Application` service, which talks
to a repository, which talks to EF Core. This keeps the database swappable
(SQL Server ↔ SQLite) and the business logic testable in isolation. `Program.cs`
is the single "composition root" where every piece is wired together.

**Step-by-step to build it:**
1. Create the solution `CustomerServiceApi.sln` and add five projects:
   `CustomerService.Api`, `CustomerService.Application`, `CustomerService.Domain`,
   `CustomerService.Infrastructure`, `CustomerService.ML` (the `.csproj` files
   already exist — see pointers below).
2. In `Domain`, define the entity interfaces (`IRepository<T>`,
   `IDashboardRepository`, `IPriorityPredictor`) — pure contracts, no EF Core
   types.
3. In `Infrastructure`, implement `Repository<T>` (generic EF Core wrapper) and
   `DashboardRepository` (the read-heavy dashboard queries).
4. In `Application`, write the services (`CustomerService`, `CaseService`, …)
   that depend on the repository interfaces, not on `AppDbContext` directly.
5. In `Api`, write `Program.cs`: register `AppDbContext` (provider chosen from
   config), register repositories and services as scoped, register
   `IPriorityPredictor` as a singleton, add JWT auth + controllers, then
   `app.Run()`.
6. Build the whole solution: `dotnet build CustomerServiceApi.sln`. A clean
   build with no errors means the layering compiles.

> ⚠️ **Common mistake:** adding a `using CustomerService.Infrastructure;` (or
> EF Core) inside `Application` or `Api` controllers and calling `AppDbContext`
> methods directly. That short-circuits the layering and makes the service
> impossible to unit-test without a real database. Always go through
> `IRepository<T>`.

> ⚠️ **Common mistake:** registering a `DbContext` or repository as a singleton.
> EF Core contexts are **not** thread-safe; they must be scoped (one per
> request). Only `IPriorityPredictor` is a singleton here, because the ONNX
> session is expensive to load and safe to share.

**📍 Find it in the code:**
- `backend/CustomerServiceApi.sln` — the solution file tying the five projects together.
- `backend/src/CustomerService.Domain/Interfaces/IRepository.cs` — generic repository contract (`Query`, `GetByIdAsync`, `AddAsync`, `Update`, `Remove`, `SaveChangesAsync`).
- `backend/src/CustomerService.Domain/Interfaces/IDashboardRepository.cs` — dashboard-specific read contract.
- `backend/src/CustomerService.Infrastructure/Repositories/Repository.cs` — `Repository<T>` implementing `IRepository<T>` over EF Core.
- `backend/src/CustomerService.Infrastructure/Repositories/DashboardRepository.cs` — `DashboardRepository : IDashboardRepository`.
- `backend/src/CustomerService.Application/Services/` — `CustomerService.cs`, `CaseService.cs`, `CallLogService.cs`, `DashboardService.cs`, `AuthService.cs`, `NotificationService.cs`.
- `backend/src/CustomerService.Api/Program.cs` — the composition root (`CreateHostBuilder` registers everything; `Main` builds, seeds, runs).

**Verified working as of:** 2026-07-20 — `dotnet build CustomerServiceApi.sln` succeeds and the API starts on `:5274` with the SQLite provider.

---

## Part 2 — Database, seeding & the SQLite fallback

**What you're building and why:** The app must be usable the instant you clone
it — no manual schema scripts, no migration step. So it uses EF Core
`EnsureCreated()` plus an **idempotent** seed that only runs when the database is
empty. The provider is configurable: `appsettings.json` says `SqlServer` by
default, but you override it to `Sqlite` with one environment variable and get a
zero-server local database (`customer_service.db`) that EF Core creates on first
run. That fallback exists so a beginner (or a CI box) can run the whole thing
without installing SQL Server.

**Step-by-step to build it:**
1. In `appsettings.json`, keep `"Database": { "Provider": "SqlServer" }` but also
   define both connection strings (`SqlServer` and `Sqlite`).
2. In `Program.cs`, read `config["Database:Provider"]`; if it equals `Sqlite`
   (case-insensitive) call `options.UseSqlite(...)`, otherwise
   `options.UseSqlServer(...)`.
3. Write `SeedData` (static lists of categories, users, customers, cases, call
   logs) and `SeedDataInitializer.Initialize(ctx)` which returns early if
   `ctx.Categories.Any()` — that's what makes it idempotent.
4. Hash the demo password with BCrypt inside the seeder (never store plaintext).
5. In `Program.cs` `Main`, after building the host, call `SeedDatabase(app)`
   (which resolves `AppDbContext` and runs `SeedDataInitializer.Initialize`).
6. Run the API with `Database__Provider=Sqlite dotnet run ...` and confirm a
   `customer_service.db` file appears and the API logs that seed data was
   inserted.

> ⚠️ **Common mistake:** running `dotnet run` with **no** provider override and
> getting a connection failure to `localhost,1433`. That's the default
> `SqlServer` provider. Either pass `Database__Provider=Sqlite`, or bring up the
> `db` service via `docker compose up`. There are **no EF Core migrations** in
> this project — `EnsureCreated()` builds the schema from the model.

> ⚠️ **Common mistake:** making the seed non-idempotent (no `if
> (ctx.Categories.Any()) return;`). Then every restart tries to re-insert and
> you get duplicate-key or unique-index crashes. The early-return guard is the
> whole trick.

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/appsettings.json` — `Database:Provider` + both connection strings.
- `backend/src/CustomerService.Api/Program.cs` — provider switch in `CreateHostBuilder`; `SeedDatabase(app)` call in `Main`.
- `backend/src/CustomerService.Infrastructure/Data/AppDbContext.cs` — the `DbSet<>` properties and `OnModelCreating` relationships.
- `backend/src/CustomerService.Infrastructure/Data/SeedData.cs` — `Categories()`, `Users()`, `Customers()`, `Cases(...)`, `CallLogs(...)`.
- `backend/src/CustomerService.Infrastructure/Data/SeedDataInitializer.cs` — `Initialize(ctx)` with the idempotent guard and BCrypt hashing (`DemoPassword = "Passw0rd!"`).

**Verified working as of:** 2026-07-20 — fresh clone + `Database__Provider=Sqlite` creates `customer_service.db` and seeds on first run; restarting does not duplicate data.

---

## Part 3 — Domain entities & enums (the data model)

**What you're building and why:** The entities are the shared vocabulary of the
whole app. `Customer` owns many `Case`s; each `Case` has a `Status` and a
`Priority` (both enums) and links to a `Category`; `CallLog` records follow-ups
per case; `User` holds auth + role; `Notification` drives the in-app bell. The
enums are serialized as **strings** in JSON (a global `JsonStringEnumConverter`
in `Program.cs`), not integers — that keeps the API self-describing and, crucially,
matches the frontend where `Case.status`/`Case.priority` are **string unions**.
Never convert them to numbers on either side.

**Step-by-step to build it:**
1. Define the entities in `Domain/Entities/`: `Customer`, `Case`, `CallLog`,
   `Category`, `User`, `Notification`.
2. Define the enums `CaseStatus` (New/InProgress/Escalated/Resolved/Closed),
   `Priority` (Low/Medium/High), `UserRole` (Agent/Admin) in the same folder.
3. In `AppDbContext.OnModelCreating`, set keys, required fields, max lengths,
   and the `Customer → Cases` cascade-delete relationship.
4. In `Program.cs`, register the global converter:
   `AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))`.
5. Confirm via Swagger (`/swagger`) that a `Case` response shows
   `"status": "New"` and `"priority": "Medium"` — words, not `0`/`1`.

> ⚠️ **Common mistake:** changing `Case.status`/`Case.priority` to numbers
> (e.g. to save bytes). The frontend types them as string unions and the global
> converter emits strings — a mismatch breaks deserialization. Keep them as
> enums serialized to strings on both ends.

> ⚠️ **Common mistake:** forgetting the global `JsonStringEnumConverter`. Then
> Swagger and the Angular `HttpClient` receive integers, and every
> `*ngIf="case.status === 'New'"` silently fails. It's registered once in
> `Program.cs` — don't re-add it per-controller.

**📍 Find it in the code:**
- `backend/src/CustomerService.Domain/Entities/Customer.cs` — customer record.
- `backend/src/CustomerService.Domain/Entities/Case.cs` — `Case` entity + the `CaseStatus` and `Priority` enums (see the `<summary>` blocks).
- `backend/src/CustomerService.Domain/Entities/CallLog.cs`, `Category.cs`, `User.cs` (incl. `UserRole`), `Notification.cs`.
- `backend/src/CustomerService.Infrastructure/Data/AppDbContext.cs` — `OnModelCreating` mappings/relationships.
- `backend/src/CustomerService.Api/Program.cs` — `AddJsonOptions(... JsonStringEnumConverter())` registration.

**Verified working as of:** 2026-07-20 — Swagger returns string enums; frontend `Case` types use string unions (see `frontend/src/app/shared/models.ts`).

---

## Part 4 — Auth: JWT login, guard & interceptor

**What you're building and why:** The API is `[Authorize]`-gated, so the SPA must
log in, hold a token, and attach it to every request. For an MVP demo we keep the
JWT in **`sessionStorage`** (not `localStorage` — it dies when the tab closes,
limiting token-theft exposure) and send it as a `Bearer` header via an HTTP
interceptor. A route guard stops unauthenticated users reaching the app. We skip
refresh tokens and httpOnly cookies on purpose — they're the "real" production
answer but add a lot of moving parts that obscure the core lesson. (The
`README.md` notes this trade-off.)

**Step-by-step to build it:**
1. Backend: `AuthController.Login` (`[AllowAnonymous]`) calls `IAuthService.LoginAsync`, returns a `LoginResponse` (token + user) or `401`.
2. Backend: `AuthService` verifies the BCrypt password hash and mints a JWT using the `Jwt:Key/Issuer/Audience` from config (registered in `Program.cs`).
3. Frontend: `auth.service.ts` `login()` POSTs to `/api/auth/login` and, on success, calls `setSession()` which writes `cs_token` + `cs_user` to `sessionStorage`.
4. Frontend: `token.interceptor.ts` clones every `HttpRequest` and adds `Authorization: Bearer <token>` (no token → request passes through unchanged).
5. Frontend: register the interceptor in `app.config.ts` via `{ provide: HTTP_INTERCEPTORS, useClass: TokenInterceptor, multi: true }` plus `provideHttpClient(withInterceptorsFromDi())`.
6. Frontend: `auth.guard.ts` (a `CanActivateFn`) returns `true` if `auth.isAuthenticated()`, else redirects to `/login`. Wire it in `app.routes.ts` on every protected route.
7. Frontend: `login.component.ts` builds a reactive form, calls `auth.login()`, and on success `router.navigate(['/dashboard'])`.
8. Verify: with no token, hit a protected endpoint → `401`; log in as `admin`/`Passw0rd!` → get a token; subsequent calls succeed.

> ⚠️ **Common mistake:** forgetting `withInterceptorsFromDi()` when calling
> `provideHttpClient(...)`. The DI-based `HTTP_INTERCEPTORS` token is then
> ignored and your `Bearer` header is never attached — every call 401s even
> after login. Both pieces must be present in `app.config.ts`.

> ⚠️ **Common mistake:** storing the token in `localStorage` "because it's
> easier." It survives tab close and is readable by any script, so an XSS bug
> steals it permanently. `sessionStorage` (used here) is the safer MVP default.

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/Controllers/AuthController.cs` — `Login` endpoint (`[AllowAnonymous]`).
- `backend/src/CustomerService.Application/Services/AuthService.cs` — password check + JWT minting (see `LoginAsync`).
- `backend/src/CustomerService.Api/Program.cs` — `AddAuthentication(JwtBearerDefaults...)` + `AddJwtBearer` validation params.
- `frontend/src/app/auth/auth.service.ts` — `login()`, `setSession()`, `getToken()`, `logout()`; token in `sessionStorage`.
- `frontend/src/app/auth/token.interceptor.ts` — `TokenInterceptor.intercept` attaches the `Bearer` header.
- `frontend/src/app/auth/auth.guard.ts` — `authGuard` `CanActivateFn`.
- `frontend/src/app/app.config.ts` — registers `HTTP_INTERCEPTORS` + `provideHttpClient(withInterceptorsFromDi())`.
- `frontend/src/app/auth/login/login.component.ts` — the login form component.

**Verified working as of:** 2026-07-20 — login as `admin`/`Passw0rd!` returns a JWT; protected endpoints return `401` without it and `200` with it.

---

## Part 5 — Customer management UI (list, detail, create/edit)

**What you're building and why:** Customers are the parent of every case, so the
CRUD surface here is the template the rest of the app reuses. The backend exposes
a thin `[Authorize]` controller that delegates to `ICustomerService`; the
frontend uses one `CustomerService` (standalone, `providedIn:'root'`) that calls
`/api/customers`. Search is server-side (`/api/customers/search?term=`) so the
list stays fast even with many rows.

**Step-by-step to build it:**
1. Backend: `CustomersController` — `GetAll`, `GetById`, `Search` (`[FromQuery] string? term`), `Create`, `Update`, `Delete`, all `[Authorize]`.
2. Backend: `CustomerService` (Application) implements those via `IRepository<Customer>`; `SearchAsync` filters on name/email/phone.
3. Frontend: `customer.service.ts` — `list()`, `search(term)`, `get(id)`, `create(dto)`, `update(dto)`, `delete(id)`, all hitting `/api/customers`.
4. Frontend: `customer-list.component` — loads `list()` (or `search()` as you type), shows a table/cards, links each row to detail, has a "New customer" button.
5. Frontend: `customer-detail.component` — `get(id)` then shows fields + the customer's cases; edit/delete actions.
6. Frontend: `customer-form.component` — reactive form used for both create and edit; on submit calls `create()` or `update()` then navigates back to the list.
7. Verify: create a customer in the UI, see it in the list, edit it, delete it; search by name narrows the list.

> ⚠️ **Common mistake:** calling the API with a trailing type mismatch — the
> backend `CustomerDto` uses **string** status/priority (Part 3), so the
> frontend `Customer` model in `shared/models.ts` must match those shapes. If
> you add a field on one side only, the form silently drops it.

> ⚠️ **Common mistake:** deleting a customer that still has cases. The
> `Customer → Cases` relationship is `OnDelete(Cascade)` (Part 3), so EF Core
> deletes the cases too — usually what you want, but know it's happening so you
> don't think cases "vanished by themselves."

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/Controllers/CustomersController.cs` — customer CRUD + search.
- `backend/src/CustomerService.Application/Services/CustomerService.cs` — `GetAllAsync`, `GetByIdAsync`, `SearchAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`.
- `frontend/src/app/customers/customer.service.ts` — the Angular HTTP service.
- `frontend/src/app/customers/customer-list.component.ts` / `.html` — list + search UI.
- `frontend/src/app/customers/customer-detail.component.ts` — detail + related cases.
- `frontend/src/app/customers/customer-form.component.ts` — create/edit form.
- `frontend/src/app/shared/models.ts` — the `Customer` / `CreateCustomer` types (string enums).

**Verified working as of:** 2026-07-20 — full customer CRUD + search works end-to-end in the running app (login `admin`/`Passw0rd!`).

---

## Part 6 — Case management UI + search/filter toolbar

**What you're building and why:** Cases are the heart of the dashboard. The
backend `CasesController` supports rich server-side filtering (status, priority,
category, date range, overdue) so the UI just passes query params. The reusable
`search-filter-toolbar` component is the filter surface used on the cases list:
a search box plus three `<mat-select>`s (status / priority / category) laid out
as a 40/60 flex split that wraps cleanly on narrow screens. Categories come from
the **frontend `CATEGORIES` constant** (there is no `/api/categories` endpoint —
see the note in `shared/categories.ts`), so keep that constant in sync with the
seed data.

**Step-by-step to build it:**
1. Backend: `CasesController.GetAll` takes `[FromQuery]` `status`, `priority`, `categoryId`, `from`, `to`, `overdue` and delegates to `CaseService.GetAllAsync(...)`.
2. Backend: `CaseService` builds the filtered query via `IRepository<Case>.Query()`; `CreateAsync` calls `IPriorityPredictor` to suggest priority (see Part 9).
3. Frontend: `case.service.ts` — `list(filters)` maps the filter object to `HttpParams` (note: `status`/`priority` are sent as **strings**, matching the enum serialization from Part 3).
4. Frontend: `search-filter-toolbar.component` — a `FormGroup` (`search`, `status`, `priority`, `category`) with `@Output()` events the parent subscribes to; the SCSS gives search `flex: 0 0 calc(40% - 6px)` and `.filters` `flex: 1 1 calc(60% - 6px)`, wrapping at `max-width: 900px`.
5. Frontend: `case-list.component` — uses the toolbar's outputs to call `caseService.list(filters)` and re-render; rows link to detail.
6. Frontend: `case-form.component` — create/edit; category `<mat-select>` is populated from `CATEGORIES`; priority can be left blank to accept the ML suggestion.
7. Frontend: `case-detail.component` — `get(id)`, shows fields, call logs, and edit/delete.
8. Verify: open Cases, type in search, change each filter — the list updates from the API; create a case without a priority and confirm one is suggested.

> ⚠️ **Common mistake:** sending `status`/`priority` as numbers from the
> frontend. The global `JsonStringEnumConverter` (Part 3) means the API expects
> the **string** (`"New"`, `"High"`). The toolbar's `<mat-option [value]="s">`
> already binds the string, so don't "helpfully" convert it to an index.

> ⚠️ **Common mistake:** Angular template `as` alias only works on the primary
> `@if`, **not** on `@else if`. If you need an alias in a branch, write
> `@else { @if (x; as y) { ... } }`. This bites people constantly when
> refactoring case-detail templates.

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/Controllers/CasesController.cs` — `GetAll` (filters), `GetById`, `Create` (ML-suggested priority).
- `backend/src/CustomerService.Application/Services/CaseService.cs` — `GetAllAsync(...)`, `CreateAsync` (calls `IPriorityPredictor`).
- `frontend/src/app/cases/case.service.ts` — `list(filters)`, `get`, `create`, `update`.
- `frontend/src/app/cases/search-filter-toolbar/search-filter-toolbar.component.ts` / `.html` / `.scss` — the reusable 40/60 filter bar.
- `frontend/src/app/cases/case-list.component.ts` — list wired to the toolbar.
- `frontend/src/app/cases/case-form.component.ts` — create/edit form (category from `CATEGORIES`).
- `frontend/src/app/cases/case-detail.component.ts` — detail view.
- `frontend/src/app/shared/categories.ts` — the `CATEGORIES` constant (ids 1–5, mirrors seed data; no API endpoint).

**Verified working as of:** 2026-07-20 — Cases list filters by status/priority/category/search end-to-end; toolbar wraps correctly at narrow widths; creating a case without priority gets a suggestion.

---

## Part 7 — Call logs & notifications

**What you're building and why:** A case isn't closed in one call — agents log
follow-ups over time, and the system should nudge them about cases whose
follow-up is overdue. Call logs are simple child records of a case
(`CallLogsController` → `ICallLogService`). Notifications are generated **on
demand** (when the client asks for them) rather than by a background worker, so
the demo runs with zero infrastructure — `NotificationService.GenerateOverdueAsync`
scans open cases with a past follow-up deadline and writes `Notification` rows
via `InAppNotificationSender`. The frontend computes the "needs follow-up" list
**live** from the cases API (`overdue: true` filter) and keeps a session-scoped
"mark all read" set, so the red badge reflects reality and resets on logout.

**Step-by-step to build it:**
1. Backend: `CallLogsController` — `GetByCase(caseId)` and `Create` (reads the
   user id from the JWT claim and passes it to `CallLogService.CreateAsync`).
2. Backend: `CallLogService` — `GetByCaseAsync`, `CreateAsync(dto, userId)`.
3. Frontend: `call-log.service.ts` — `listByCase(caseId)`, `create(dto)` → `/api/calllogs`.
4. Frontend: surface call logs inside `case-detail.component` (list + add form).
5. Backend: `NotificationsController` — `GetSummary` / `GetAll` first call
   `GenerateOverdueAsync()`, plus `MarkRead(id)`.
6. Backend: `NotificationService` + `InAppNotificationSender` (the only
   `INotificationSender` implementation — Email/SMS can be added later behind
   the same interface).
7. Frontend: `notification-state.service.ts` — `refresh()` calls
   `caseService.list({ overdue: true })`, holds the live list in a signal, and
   tracks `readIds` in `sessionStorage` (`cs_read_overdue_ids`) for the badge.
8. Frontend: `notification-bell.component` — shows the unread count badge and a
   dropdown listing overdue cases; "mark all read" clears the badge for the session.
9. Verify: open a case, add a call log, see it listed; let a case go overdue
   (seed data already has some), open the bell, see it, mark all read (badge
   clears), log out and back in (badge returns).

> ⚠️ **Common mistake:** assuming notifications are pushed by a background job.
> They're generated **on read** (`GenerateOverdueAsync` runs inside the
> `GetSummary`/`GetAll` endpoints). If you add a new overdue condition, callers
> still get fresh data without any scheduler — but don't go looking for a
> worker that doesn't exist.

> ⚠️ **Common mistake:** persisting "read" state in the database. Here "mark all
> read" is **session-scoped** (`sessionStorage`), deliberately — the unit of
> acknowledgement is the whole session, and it resets on logout so genuinely
> still-overdue cases re-alert. Don't "fix" this by writing read state to the
> server; that changes the intended behavior.

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/Controllers/CallLogsController.cs` — call-log endpoints.
- `backend/src/CustomerService.Application/Services/CallLogService.cs` — `GetByCaseAsync`, `CreateAsync`.
- `backend/src/CustomerService.Api/Controllers/NotificationsController.cs` — `GetSummary`/`GetAll` (call `GenerateOverdueAsync`), `MarkRead`.
- `backend/src/CustomerService.Application/Services/NotificationService.cs` — `GenerateOverdueAsync`, `GetSummaryAsync`.
- `backend/src/CustomerService.Application/Services/InAppNotificationSender.cs` — `INotificationSender` implementation (persists `Notification`).
- `frontend/src/app/cases/call-log.service.ts` — Angular call-log HTTP service.
- `frontend/src/app/shared/notification-state.service.ts` — live overdue list + session-scoped read set.
- `frontend/src/app/shared/notification-bell.component.ts` — the bell UI + badge.
- `frontend/src/app/shared/models.ts` — `CallLog`, `CreateCallLog`, `OverdueCase`, `Notification` types.

**Verified working as of:** 2026-07-20 — call logs add/list on a case; notification bell shows overdue cases and clears on "mark all read", returns after logout.

---

## Part 8 — Dashboard with charts

**What you're building and why:** The dashboard is the "why bother" screen — it
turns the raw cases into KPIs (total / pending / resolved), a weekly trend line,
a priority donut, and category/status bars. The backend does the aggregation
(`DashboardService.GetDashboardAsync`) and returns one `DashboardDto`; the
frontend renders it with **Chart.js via ng2-charts**. The critical, easy-to-miss
detail: Chart.js components must be **registered explicitly** in `main.ts`, or
you get cryptic "not a registered scale/controller" or "Filler plugin" errors at
runtime. The dashboard also hooks the app-shell sidenav state so the page brand
logo only animates on an explicit toggle (see Part 10).

**Step-by-step to build it:**
1. Backend: `DashboardController.Get()` → `IDashboardService.GetDashboardAsync()` returns KPIs + `WeeklyTrend` + `CategoryBreakdown` + `StatusBreakdown` + `RecentCases`.
2. Backend: `DashboardService` builds those from `IRepository<Case>` (counts, groupings, last-N recent).
3. Frontend: `dashboard.service.ts` — `get()` → `/api/dashboard`.
4. Frontend: `main.ts` — `Chart.register(CategoryScale, LinearScale, PointElement, LineElement, BarElement, ArcElement, LineController, BarController, DoughnutController, Tooltip, Legend, Title, Filler)`. **Do not skip any used here.**
5. Frontend: `dashboard.component.ts` — declares `trendChart` (line), `priorityChart` (doughnut), `categoryChart` (horizontal bar), `statusChart` (bar) as `ChartConfiguration` objects, feeds them from the `Dashboard` signal, and uses `BaseChartDirective` (ng2-charts) in the template.
6. Frontend: `dashboard.component.html` — KPI cards + `<canvas baseChart ...>` for each chart, wrapped with the `RevealDirective` for subtle scroll-in.
7. Verify: load `/dashboard` (login first) — KPIs populate and all four charts render with no console errors.

> ⚠️ **Common mistake:** forgetting to register Chart.js pieces in `main.ts`.
> ng2-charts does **not** auto-register them. Omit `Filler` and area/line fills
> throw; omit `ArcElement`/`DoughnutController` and the donut vanishes; omit a
> scale and you get "not a registered scale". Register every piece you use, once,
> at startup. (AGENTS.md calls this out explicitly.)

> ⚠️ **Common mistake:** computing the dashboard aggregation on the client. The
> counts/trends come from the **API** (`DashboardService`), not by fetching all
> cases and reducing in the browser. Keep heavy aggregation server-side so the
> payload stays small.

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/Controllers/DashboardController.cs` — `Get()` dashboard endpoint.
- `backend/src/CustomerService.Application/Services/DashboardService.cs` — `GetDashboardAsync` (KPIs, trends, breakdowns, recent).
- `backend/src/CustomerService.Infrastructure/Repositories/DashboardRepository.cs` — the read queries behind the service.
- `frontend/src/main.ts` — `Chart.register(...)` of all required Chart.js pieces.
- `frontend/src/app/dashboard/dashboard.service.ts` — Angular dashboard HTTP service.
- `frontend/src/app/dashboard/dashboard.component.ts` / `.html` — KPI cards + the four `baseChart` canvases.
- `frontend/src/app/shared/models.ts` — the `Dashboard` / `RecentCase` types.

**Verified working as of:** 2026-07-20 — `/dashboard` renders KPI cards and all four charts (line/donut/bar/status) with Chart.js registered in `main.ts`; no console errors.

---

## Part 9 — AI priority prediction (backend ML wiring)

**What you're building and why:** When an agent creates a case without picking a
priority, the system suggests one (Low / Medium / High) so nothing sits
un-triaged. The backend defines a tiny `IPriorityPredictor` abstraction with two
implementations: `OnnxPriorityPredictor` (loads `ml/models/priority_model.onnx`
and runs it via ONNX Runtime) and `RuleBasedPriorityPredictor` (a deterministic,
dependency-free lexicon scorer). `Program.cs` registers `IPriorityPredictor` as a
**singleton** and resolves the ONNX model path against the content root (and the
repo root) so it's found no matter where you launch the API from. **Crucially,
if the `.onnx` file is missing the app silently falls back to the rules** — so
the whole backend runs with zero Python/ML setup. The model input is 4 floats in
a fixed order: `[categoryId, priorCaseCount, daysSinceLastContact, sentiment]`;
`sentiment` is a lexicon score in [-1, 1] computed from the description by
`RuleBasedPriorityPredictor.SentimentScore` (it mirrors the Python `sentiment_score`).

**Step-by-step to build it:**
1. Define `PriorityFeatures` (4 inputs) and `IPriorityPredictor` (+ `PriorityPredictionResult`, `PriorityModelSource`) in `Domain/Interfaces`.
2. Implement `RuleBasedPriorityPredictor` — `SentimentScore(description)` plus `PredictWithReason` using the negative/positive lexicons and the `daysSince`/`priorCaseCount` thresholds. This is the always-available fallback.
3. Implement `OnnxPriorityPredictor` — in `PredictWithReason`, build the 4-float tensor in the exact order above, name it `"input"`, run the session, read the `"probabilities"` output (order `[Low, Medium, High]`), and pick the argmax. If `_session is null`, delegate to the rule-based fallback.
4. In `Program.cs`, register `IPriorityPredictor` as a singleton; resolve `ML:ModelPath` against `ContentRootPath` and the solution root; log a warning (not an error) when the model is absent.
5. In `CaseService.CreateAsync`, compute `priorCaseCount`, `daysSinceLastContact`, and `sentiment`, then call `_predictor.PredictWithReason(...)` **only when `dto.Priority` is not supplied**; store `PriorityAutoSuggested = true` and the `Reason`.
6. Verify: with **no** `.onnx` present, create a case with an angry description and no priority → it gets a suggested priority + reason (rule-based). With the model present (Part 10), the same call uses ONNX.

> ⚠️ **Common mistake:** getting the feature order wrong. The ONNX model was
> trained expecting `[categoryId, priorCaseCount, daysSinceLastContact, sentiment]`
> (see `train_model.py`). If you reorder them in `OnnxPriorityPredictor`, the
> model "works" but predicts garbage because it's reading the wrong column as
> sentiment. Keep the order identical on both sides.

> ⚠️ **Common mistake:** treating a missing model as a crash. The design
> deliberately logs a *warning* and falls back to rules. If you see priority
> suggestions but no `priority_model.onnx` in the repo, that's expected — the
> file is git-ignored and regenerated by the Python pipeline (Part 10). Don't
> "fix" the absence by hard-failing startup.

**📍 Find it in the code:**
- `backend/src/CustomerService.Domain/Interfaces/IPriorityPredictor.cs` — `PriorityFeatures`, `IPriorityPredictor`, `PriorityPredictionResult`, `PriorityModelSource`.
- `backend/src/CustomerService.ML/RuleBasedPriorityPredictor.cs` — lexicon + `SentimentScore` + `PredictWithReason` (the fallback).
- `backend/src/CustomerService.ML/OnnxPriorityPredictor.cs` — `PredictWithReason` (tensor build + session run + fallback).
- `backend/src/CustomerService.Api/Program.cs` — singleton registration + model-path resolution (see the `ResolveModelPath` lambda).
- `backend/src/CustomerService.Application/Services/CaseService.cs` — `CreateAsync` calls the predictor only when `dto.Priority` is null (see the `priority = dto.Priority ?? prediction!.Priority` line).
- `backend/src/CustomerService.Api/appsettings.json` — `ML:ModelPath` (`ml/models/priority_model.onnx`).

**Verified working as of:** 2026-07-20 — creating a case with no priority returns a suggested `Priority` + `PriorityReason`; with no `.onnx` the rule-based fallback is used (logged as a warning, not an error).

---

## Part 10 — The Python ML pipeline (train the ONNX model)

**What you're building and why:** This is the offline half of the "AI" — a
Python script that synthesizes a labeled dataset, trains a small classifier, and
**exports it to ONNX** so the C# backend can run it without Python at runtime.
The dataset is synthetic and *rule-labeled* (there's no real historical data),
which `docs/MODEL_CARD.md` documents as a known limitation — but it's enough to
demonstrate the full train→export→infer loop. You run this in a **venv** (the
system Python is externally managed). The output `ml/models/priority_model.onnx`
is git-ignored, so every clone regenerates it. `clean_data.py` is a separate,
reusable utility that scrubs a messy CRM/Excel export (the kind of raw data you'd
feed a real model later).

**Step-by-step to build it:**
1. Create and activate the venv, then install deps:
   ```bash
   python3 -m venv ml/.venv
   source ml/.venv/bin/activate
   pip install -r ml/requirements.txt
   ```
2. (Optional) Run `python ml/clean_data.py` to see the cleaning pipeline: it
   reads a raw export, normalizes phones/emails/dates, fuzzy-matches categories,
   flags (not drops) rows missing required fields into `rejected_rows.csv`, and
   writes `ml/data/cleaned/cases_cleaned.csv`.
3. Run `python ml/train_model.py`. It generates the synthetic dataset, splits
   train/test, trains a `DecisionTreeClassifier`, prints accuracy + a
   classification report, and exports `ml/models/priority_model.onnx` (opset 17,
   integer labels 0/1/2 → Low/Medium/High).
4. Confirm `ml/models/priority_model.onnx` now exists. Restart the backend (it
   resolves the path at startup) — the API log should now say "ML-based priority
   suggestions enabled" instead of the fallback warning.
5. Verify end-to-end: create a case with an urgent description and no priority →
   the suggestion now comes from ONNX (`PriorityModelSource.Onnx`).

> ⚠️ **Common mistake:** running the scripts against the **system** Python. On
> many distros that's externally managed and `pip install` is blocked. Always
> `source ml/.venv/bin/activate` first. The `ml/models/*.onnx` file is
> git-ignored — if it's "missing" after a clone, that's normal; regenerate it
> with `train_model.py`.

> ⚠️ **Common mistake:** changing the feature order or the label mapping in
> `train_model.py` without updating `OnnxPriorityPredictor` (Part 9). The
> contract is: 4-float input `[categoryId, priorCaseCount, daysSinceLastContact,
> sentiment]` and a 3-element output in `[Low, Medium, High]` order. Break either
> side and inference silently degrades. Keep `MODEL_CARD.md` in sync if you do
> change it.

**📍 Find it in the code:**
- `ml/requirements.txt` — pinned deps (pandas 2.2.2, scikit-learn 1.5.1, skl2onnx 1.20.0, onnxruntime 1.18.1, …); recommends Python 3.12.
- `ml/train_model.py` — synthetic data + `label_rule` + `DecisionTreeClassifier` + ONNX export. Documents the exact 4-feature input order and `[Low, Medium, High]` output order in its module docstring.
- `ml/clean_data.py` — the reusable CRM/Excel cleaning utility (normalize phones/emails/dates, fuzzy category match, `rejected_rows.csv`).
- `ml/data/` — `raw_cases.csv` (sample messy input) and `cleaned/` (output of `clean_data.py`).
- `ml/models/priority_model.onnx` — the generated model (git-ignored; produced by `train_model.py`).
- `docs/MODEL_CARD.md` — the model's documented limitations (synthetic, rule-labeled data).

**Verified working as of:** 2026-07-20 — `ml/train_model.py` runs in the venv and exports `ml/models/priority_model.onnx`; with it present the backend logs "ML-based priority suggestions enabled" and `CaseService.CreateAsync` uses the ONNX source.

---

## Part 11 — App shell, layout & design system

**What you're building and why:** This is the polish that makes it feel like a
product, not a prototype. A single `LayoutComponent` is the shell: a `mat-sidenav`
that is `side` mode on desktop (pushes content) and `over` (overlay) mode on
narrow screens (`<768px`) with a dim backdrop and a thin icon **rail** underneath.
All guarded routes render inside its `<router-outlet>`. The look is an
Apple-like system: generous whitespace, rounded corners, restrained indigo
accent, system font stack, and *subtle* motion only (hover lift, scroll reveal)
— never flashy. Icons come from `lucide-angular` via a small `CsIconComponent`
that renders SVGs by name. The rail/sidenav behavior has specific, verified
rules (instant rail on close, constant content padding in overlay mode) so it
never "pops."

**Step-by-step to build it:**
1. `app.routes.ts` — a top-level `LayoutComponent` route with `canActivate: [authGuard]` wrapping child routes (`dashboard`, `customers`, `cases`); `login` is outside the guard; `**` redirects to `dashboard`.
2. `layout.component.ts` — `isHandset` signal from `window.matchMedia('(max-width: 767px)')` + a `BreakpointObserver`; `opened` signal (starts open on desktop, closed on handset); `brandAnimate` true only briefly after a toggle.
3. `layout.component.html` — `mat-sidenav [mode]="isHandset() ? 'over' : 'side'" [opened]="opened()"` with `(openedChange)` syncing state (so a backdrop click closes cleanly to the rail); a `@if (!opened())` thin `.rail` with toggle/bell/nav; content gets `[class.sidebar-closed]="!opened() || isHandset()"` for constant padding in overlay mode.
4. `cs-icon.component.ts` — imports the Lucide icons you use, maps a `name` → SVG, and implements `OnChanges` so the glyph re-renders when `name` changes (otherwise a dynamic icon like the toggle's chevron/menu sticks).
5. `reveal.directive.ts` — `[appReveal]` adds a one-shot IntersectionObserver fade+rise (`.reveal` class in `styles.scss`).
6. `styles.scss` — `:root` design tokens (`--cs-bg`, `--cs-accent`, `--cs-radius`, `--cs-ease`, …), the `.reveal`/`.cs-lift`/`.stagger` utilities, and a `@media (prefers-reduced-motion: reduce)` block that disables them.
7. Verify: resize the window narrow → sidenav auto-hides to the rail; open it (overlay + dim backdrop) and click the backdrop → it closes with **no** slide/pop and the content doesn't jump; toggle the sidenav → the brand logo animates only then.

> ⚠️ **Common mistake:** Angular template `as` alias works **only on the
> primary `@if`**, not on `@else if`. If you need an alias in a branch, write
> `@else { @if (x; as y) { ... } }`. This is the single most common template
> compile error in this project.

> ⚠️ **Common mistake:** forgetting `OnChanges` in `cs-icon.component.ts`. The
> icon only rendered in `ngOnInit`, so a dynamically-bound name (chevron_left ↔
> menu on the toggle) stayed frozen until a refresh. Implement `ngOnChanges` to
> re-render on `name` change.

> ⚠️ **Common mistake:** leaving a `transition` on the `.rail` or the sidenav
> close animation. In overlay mode the rail must appear **instantly**
> (`transition: none`) and the sidenav must not slide (`transition: none` under
> `.sidenav-overlay`) — otherwise closing via backdrop "pops" the rail from left
> to right. This was fixed in Phase 19.6; don't reintroduce those transitions.

**📍 Find it in the code:**
- `frontend/src/app/app.routes.ts` — route tree (guard + `LayoutComponent` + children).
- `frontend/src/app/shared/layout/layout.component.ts` / `.html` / `.scss` — the shell, sidenav/rail, overlay vs side modes, instant-rail fix.
- `frontend/src/app/shared/cs-icon.component.ts` — Lucide icon renderer (with `OnChanges`).
- `frontend/src/app/shared/reveal.directive.ts` — scroll-reveal directive.
- `frontend/src/app/shared/categories.ts` — the `CATEGORIES` constant (used by forms/toolbar).
- `frontend/src/styles.scss` — design tokens, `.reveal`/`.cs-lift`/`.stagger`, reduced-motion block.
- `frontend/src/main.ts` — Chart.js registration (Part 8) + bootstrap.

**Verified working as of:** 2026-07-20 — narrow viewport auto-hides to the rail; backdrop close is instant with no pop and constant content padding; toggle animates the brand logo only.

---

## Part 12 — Running, testing & building for production

**What you're building and why:** The app is only "done" when you can run it,
prove it works, and ship it. Locally you run the backend (SQLite override, zero
config) and the Angular dev server (which proxies `/api` → `:5274` via
`proxy.conf.json`). Tests give you a safety net: `dotnet test` (backend, 24
tests) and `ng test` (frontend, 13 tests, headless Chrome). For production you
`ng build` (budget warning at 1.5 MB, hard error at 2.5 MB) and optionally
`docker compose up` which builds SQL Server + API + Nginx in one command.

**Step-by-step to build it:**
1. **Backend (SQLite, zero config):** from `backend/`,
   ```bash
   DOTNET_ENVIRONMENT=Development Database__Provider=Sqlite \
     dotnet run --project src/CustomerService.Api/CustomerService.Api.csproj \
     --urls "http://localhost:5274"
   ```
   First run creates `customer_service.db` and seeds it. Swagger at `/swagger`.
2. **Frontend dev server:** from `frontend/`, `npm install` then `npm start`
   (→ `:4200`). `proxy.conf.json` forwards `/api` to `:5274`, so no CORS fuss.
3. **Log in:** open `http://localhost:4200`, sign in as `admin` / `Passw0rd!`.
4. **Backend tests:** `dotnet test CustomerServiceApi.sln` (expect 24 passing).
5. **Frontend tests:** `CHROME_BIN=$(which google-chrome) npx ng test --watch=false --browsers=ChromeHeadlessCI` (expect 13 passing; Chrome must be installed).
6. **Production build:** `npm run build` → `dist/`. Check the bundle-size budget line in the output.
7. **One-command stack (optional):** `docker compose up --build` brings up SQL Server + API + Nginx at `http://localhost:8080` (API uses SQL Server here; see `docker-compose.yml`).

> ⚠️ **Common mistake:** running the frontend without the backend (or with the
> wrong port). `npm start` serves `:4200` but every API call 404s/401s unless
> `:5274` is up. Keep both running. The proxy only rewrites `/api`, so open the
> app at `:4200`, not `:5274`.

> ⚠️ **Common mistake:** ignoring the Angular build budget. A warning at 1.5 MB
> initial is fine; an **error at 2.5 MB fails the build**. If you add a heavy
> dependency, lazy-load it or raise the budget in `angular.json` deliberately —
> don't be surprised when CI goes red.

> ⚠️ **Common mistake:** running `dotnet test` against the default `SqlServer`
> provider. The tests expect the SQLite fallback — pass `Database__Provider=Sqlite`
> (or set it in `appsettings.Development.json`) or they'll fail connecting to
> `localhost,1433`.

**📍 Find it in the code:**
- `backend/src/CustomerService.Api/appsettings.json` + `appsettings.Development.json` — provider + connection strings.
- `frontend/proxy.conf.json` — `/api` → `http://localhost:5274`.
- `frontend/package.json` — `start` / `build` / `test` scripts.
- `frontend/angular.json` — build budgets (1.5 MB warn / 2.5 MB error).
- `docker-compose.yml` — SQL Server + API + Nginx one-command stack.
- `backend/tests/CustomerService.Tests/` — the 24 backend tests (`CaseServiceTests`, `PredictorTests`, …).
- `frontend/src/**/*.spec.ts` — the 13 frontend tests (e.g. `auth.guard.spec.ts`, `token.interceptor.spec.ts`, `dashboard.component.spec.ts`).

**Verified working as of:** 2026-07-20 — `dotnet test` passes 24/24 (SQLite); `ng test` passes 13/13 (headless Chrome); `npm run build` succeeds; `docker compose up` builds the full stack.

---

*End of DIY guide. Keep this document current: whenever a future change touches a
feature documented above, update the relevant Part, add a dated one-line
"Revision note" under it, and refresh any inline code comments whose §-reference
is now inaccurate. See the standing maintenance rule at the top of this file's
task brief.*
