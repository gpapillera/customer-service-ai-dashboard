# Code Documentation — Customer Service AI Dashboard

Reference for the codebase structure, conventions, and how the pieces fit together.
For a chronological build history see [`PROGRESS_LOG.md`](./PROGRESS_LOG.md). For the ML
model specifics see [`MODEL_CARD.md`](./MODEL_CARD.md). For a high-level overview see the
root [`README.md`](../README.md).

---

## 1. Repository layout

```
customer-service-ai-dashboard/
├── AGENTS.md                 # Guidance for AI coding agents (read first)
├── MVP_BUILD_PROMPT.md       # Original spec / design brief
├── README.md                 # High-level overview + getting started
├── backend/                  # ASP.NET Core 8 Web API
│   ├── CustomerServiceApi.sln
│   ├── src/
│   │   ├── CustomerService.Api/           # Composition root + Controllers
│   │   ├── CustomerService.Application/   # DTOs, Interfaces, Services (business logic)
│   │   ├── CustomerService.Domain/        # Entities, enums, shared contracts (IPriorityPredictor)
│   │   ├── CustomerService.Infrastructure/# EF Core DbContext, Repositories, Seed
│   │   └── CustomerService.ML/            # OnnxPriorityPredictor + RuleBasedPriorityPredictor
│   └── tests/CustomerService.Tests/       # xUnit (placeholder UnitTest1 only)
├── frontend/                 # Angular 18 standalone SPA
│   └── src/app/
│       ├── app.config.ts / app.routes.ts / app.component.*
│       ├── auth/             # login, auth.service, auth.guard, token.interceptor
│       ├── shared/           # layout, cs-icon, categories, models, reveal.directive
│       ├── dashboard/        # KPI cards + charts
│       ├── customers/        # list / detail / form + customer.service
│       └── cases/            # list / detail / form + case.service, call-log.service
├── ml/                       # Python data cleaning + model training
│   ├── clean_data.py, train_model.py, requirements.txt
│   ├── data/                 # raw_cases.csv, cleaned/
│   └── models/priority_model.onnx   # generated, gitignored
├── database/                 # install_sqlserver.sh, schema.sql, sqlserver-data/
└── docs/                     # PROGRESS_LOG.md, MODEL_CARD.md, CODE_DOCUMENTATION.md
```

---

## 2. Backend (ASP.NET Core 8)

### Architecture & layering
Strict dependency direction (no cycles):

```
Api ──▶ Application ──▶ Domain ◀── Infrastructure
 │           │            ▲            │
 │           │            │            ▼
 └───────────┴────────────┘        ML ──▶ Domain
```

- **Controllers** (`CustomerService.Api/Controllers`): thin HTTP layer, attribute routing,
  `[ApiController]`, JWT-protected via `[Authorize]` (except `AuthController.Login`).
- **Application** (`CustomerService.Application`): `Services/*Service.cs` hold business logic;
  interfaces in `Interfaces/`; DTOs in `Dtos/`.
- **Domain** (`CustomerService.Domain`): EF Core entities in `Entities/`, enums, and the shared
  ML contract (`IPriorityPredictor`, `PriorityFeatures`).
- **Infrastructure** (`CustomerService.Infrastructure`): `Data/AppDbContext`, generic
  `Repository<T>`, `DashboardRepository`, idempotent seeder.
- **ML** (`CustomerService.ML`): `OnnxPriorityPredictor` (loads `priority_model.onnx`) with a
  `RuleBasedPriorityPredictor` fallback when the model file is absent.

### Service registration (`Program.cs`)
- `IRepository<T>` → `Repository<T>` (scoped)
- `IDashboardRepository` → `DashboardRepository` (scoped)
- `ICustomerService`, `ICaseService`, `ICallLogService`, `IAuthService`, `IDashboardService`
  (scoped)
- `IPriorityPredictor` → `OnnxPriorityPredictor` (singleton; falls back to rule-based)
- `AddControllers()` registers `JsonStringEnumConverter` globally → enums serialize as **strings**
  (`Open`, `High`, etc.). The frontend relies on this; do not switch to numeric enums.

### Database providers
Controlled by `Database:Provider` in `appsettings.json` / env var `Database__Provider`:
- `SqlServer` (default) — needs a running SQL Server instance.
- `Sqlite` — zero-dependency fallback; connection string `Data Source=customer_service.db`.
  The app uses `EnsureCreated()` + idempotent seed (no EF migrations). First run is slow
  (schema create + seed + ONNX session load).

### Auth
- JWT Bearer (HS256). Key/Issuer/Audience in `Jwt:*` config.
- Roles: `Admin`, `Agent`. Demo users (BCrypt-hashed at seed): `admin`, `agent`, `maria` —
  all password `Passw0rd!`.
- `AuthController.Login` (`POST /api/auth/login`, `[AllowAnonymous]`) returns
  `{ token, expiresUtc, userName, fullName, role }`.

### REST API (all under `/api`, JWT required except login)

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous | Authenticate → JWT |
| GET | `/api/customers` | User | List all customers |
| GET | `/api/customers/search?term=` | User | Search by name/email/phone |
| GET | `/api/customers/{id}` | User | Customer detail |
| POST | `/api/customers` | User | Create customer |
| PUT | `/api/customers/{id}` | User | Update customer |
| DELETE | `/api/customers/{id}` | User | Delete customer |
| GET | `/api/cases` | User | List with filters (`status`, `priority`, `categoryId`, `from`, `to`) |
| GET | `/api/cases/{id}` | User | Case detail |
| POST | `/api/cases` | User | Create case (priority ML-suggested if omitted) |
| PUT | `/api/cases/{id}` | User | Update case (priority override allowed) |
| DELETE | `/api/cases/{id}` | User | Delete case |
| GET | `/api/calllogs/case/{caseId}` | User | Call logs for a case |
| POST | `/api/calllogs` | User | Add a call log |
| GET | `/api/dashboard` | User | KPI totals + 30-day trend + category breakdown |

> There is **no** `/api/categories` endpoint. Categories are a frontend constant
> (`frontend/src/app/shared/categories.ts`, ids 1–5) kept in sync with seed data.

### ML wiring
- `CaseService.CreateAsync` calls `IPriorityPredictor.Predict(features)` where
  `features = [categoryId, priorCaseCount, daysSinceLastContact, hasComplaintKeyword]`.
- Output order is `[Low, Medium, High]`; the highest-probability class wins and
  `PriorityAutoSuggested` is set `true`. Agents can override on update.
- If `priority_model.onnx` is missing, prediction silently falls back to rules.

---

## 3. Frontend (Angular 18, standalone)

### Conventions
- **Standalone components only** (no NgModules). Routes in `app.routes.ts`.
- HTTP services are `providedIn:'root'`, inject `HttpClient`, and call `/api/...`.
- The dev server proxies `/api` → `http://localhost:5274` (`proxy.conf.json`).
- `auth.service.ts` stores the JWT in `sessionStorage`; `token.interceptor.ts` attaches it;
  `auth.guard.ts` guards all non-`/login` routes.

### Design system (`styles.scss`)
Apple-like: system font stack, `#f5f5f7` background, white surfaces, `#0071e3` accent
(`--cs-accent`), 18px radii (`--cs-radius`), soft shadows, `cubic-bezier(0.22,1,0.36,1)` easing.
Animation utilities: `.reveal` / `.cs-lift` / `.stagger` + `RevealDirective` (`[appReveal]`)
for scroll reveal. `prefers-reduced-motion` is respected.

### Icons
`shared/cs-icon.component.ts` (`<cs-icon name="...">`) maps old Material icon names to bundled
Lucide SVGs (`lucide-angular`, npm dependency — **no runtime CDN**). Do not reintroduce
`<mat-icon>` + a Google Fonts `<link>`.

### Charts
Chart.js must be registered in `main.ts` (`Chart.register(...)` with `CategoryScale`,
`LinearScale`, `PointElement`, `LineElement`, `BarElement`, `LineController`, `BarController`,
`Tooltip`, `Legend`, `Title`, `Filler`). Omitting these breaks ng2-charts.

### Known frontend quirks
- Angular template `as` alias works only on the primary `@if`, not `@else if`
  (use `@else { @if (x; as y) { ... } }`).
- `Case.status` / `Case.priority` are **string unions** on both sides — never convert to numbers.
- `NG0912` Lucide component-ID collision warning is cosmetic (library-internal).

---

## 4. ML pipeline (Python)

Run inside a venv (system Python is externally-managed):

```bash
cd ml
python3 -m venv .venv          # requires apt package python3-venv
source .venv/bin/activate
pip install -r requirements.txt
python clean_data.py           # raw_cases.csv -> data/cleaned/cases_cleaned.csv (+ rejected_rows.csv)
python train_model.py          # -> models/priority_model.onnx (opset 17, int labels 0/1/2)
```

- `clean_data.py`: dedup, phone/email normalization, date parsing → ISO 8601, missing-field
  rows flagged to `rejected_rows.csv` (not dropped).
- `train_model.py`: synthetic, rule-labeled data → `DecisionTreeClassifier` → ONNX export.
  Training data is synthetic; retrain on real data before any production use.

---

## 5. Running the project (verified local setup)

Backend (SQLite fallback, no SQL Server needed):
```bash
cd backend
DOTNET_ENVIRONMENT=Development Database__Provider=Sqlite \
  dotnet run --project src/CustomerService.Api/CustomerService.Api.csproj --urls "http://localhost:5274"
```
Frontend:
```bash
cd frontend
npm install   # already done in this workspace
npm start     # ng serve -> http://localhost:4200
```
Sign in with `admin` / `Passw0rd!`. Swagger UI at `http://localhost:5274/swagger` (Development).

---

## 6. Testing status
- Backend: `tests/CustomerService.Tests` exists but contains only a placeholder `UnitTest1`.
- Frontend: no automated tests yet.
- Both build clean (`dotnet build`, `npm run build`).
