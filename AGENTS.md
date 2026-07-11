# AGENTS.md — Customer Service AI Dashboard

Guidance for AI coding agents working in this repo. Keep changes runnable and documented (see `docs/PROGRESS_LOG.md`).

## What this is
Full-stack demo: Angular 18 SPA → ASP.NET Core 8 Web API → SQL Server (SQLite fallback), with an offline Python ML pipeline that exports an ONNX priority model loaded by the backend. Public docs: [`README.md`](./README.md). Spec/design: [`MVP_BUILD_PROMPT.md`](./MVP_BUILD_PROMPT.md). Build history: [`docs/PROGRESS_LOG.md`](./docs/PROGRESS_LOG.md). ML details: [`docs/MODEL_CARD.md`](./docs/MODEL_CARD.md).

## Repo layout
- `backend/` — ASP.NET Core solution `CustomerServiceApi.sln` (projects: `Api`, `Application`, `Domain`, `Infrastructure`, `ML`; tests in `tests/`).
- `frontend/` — Angular 18 standalone app (dev server `:4200`, proxies `/api` → `:5274`).
- `ml/` — Python scripts (`clean_data.py`, `train_model.py`) + `models/priority_model.onnx` (gitignored).
- `database/` — `install_sqlserver.sh` + `schema.sql`.
- `docs/` — `PROGRESS_LOG.md`, `MODEL_CARD.md`.

## Commands
**Backend** (from `backend/`): build `dotnet build CustomerServiceApi.sln`; run `dotnet run --project src/CustomerService.Api/CustomerService.Api.csproj --urls "http://localhost:5274"`; test `dotnet test CustomerServiceApi.sln`. Swagger at `/swagger` (Dev only).
**Frontend** (from `frontend/`): `npm install`; dev `npm start` (→ `:4200`); prod build `npm run build`; tests `npm test`.
**ML** (system Python is externally-managed — use the venv): `source ml/.venv/bin/activate` then `python ml/clean_data.py` and `python ml/train_model.py`. Create venv with `python3 -m venv ml/.venv` (requires apt package `python3-venv`).

## Architecture & conventions
- **Backend layering:** `Controllers` → `Application/Services` (interface in `Application/Interfaces`) → `Infrastructure/Repositories` (`IRepository<T>`, `IDashboardRepository`) → `Domain/Entities` (EF Core). Add a feature by extending each layer; register services as scoped and the `IPriorityPredictor` as a singleton in `Program.cs`.
- **Enums serialize as strings.** `Program.cs` registers `JsonStringEnumConverter` globally. Frontend `Case.status`/`Case.priority` are **string unions** — never convert them to numbers on either side.
- **ML wiring:** `CaseService.CreateAsync` calls `IPriorityPredictor` (backend `OnnxPriorityPredictor`, falls back to `RuleBasedPriorityPredictor` if the `.onnx` is missing). Model input = 4 floats `[categoryId, priorCaseCount, daysSinceLastContact, hasComplaintKeyword]`; output order `[Low, Medium, High]`. Retrain via `ml/train_model.py` (opset 17, integer labels 0/1/2).
- **Auth:** JWT Bearer, roles `Admin`/`Agent`. Demo users `admin`, `agent`, `maria` — all password `Passw0rd!`. Frontend stores JWT in `sessionStorage` via `auth.service.ts` + `token.interceptor.ts`; `auth.guard.ts` guards all non-login routes.
- **DB:** `EnsureCreated()` + idempotent seed at startup (no EF migrations). Provider via `Database:Provider` (`SqlServer` default, else `Sqlite`).

## Frontend specifics
- **Standalone components only** (no NgModules). Routes in `app.routes.ts`; HTTP services in `*/<feature>.service.ts` (`providedIn:'root'`, `inject(HttpClient)`).
- **Apple-like design system** in `styles.scss`: CSS vars (`--cs-bg`, `--cs-accent`, `--cs-radius`, `--cs-ease`), utility classes `.reveal`/`.cs-lift`/`.stagger`, and `@media (prefers-reduced-motion: reduce)` support. Use `RevealDirective` (`[appReveal]`) for scroll reveal. Keep UI simple with subtle hover/scroll motion only.
- **Categories are a frontend constant** in `shared/categories.ts` (`CATEGORIES`, ids 1–5). There is **no `/api/categories` endpoint** — keep the constant in sync with seed data.
- **Chart.js must be registered** in `main.ts` (`Chart.register(...)` with `CategoryScale`, `LinearScale`, `PointElement`, `LineElement`, `BarElement`, `LineController`, `BarController`, `Tooltip`, `Legend`, `Title`, `Filler`). Omitting these breaks ng2-charts with "not a registered scale" / "Filler plugin" errors.

## Pitfalls to avoid
- Angular template `as` alias works **only on the primary `@if`**, not `@else if` (use `@else { @if (x; as y) { ... } }`).
- Don't change `Case.status`/`Case.priority` to numbers (see enums note above).
- First backend run is slow (ONNX session load + seed). If `priority_model.onnx` is absent, prediction silently falls back to rules.
- `ml/models/*.onnx` is gitignored — regenerate, don't expect it committed.
- Prod build budget warning at 1.5 MB initial is non-fatal; error at 2.5 MB fails the build (`angular.json`).

## Before finishing a task
- Keep `docs/PROGRESS_LOG.md` updated (newest-on-top entry per phase).
- Verify with a build (`dotnet build` / `npm run build`) and, for UI, a browser check at `http://localhost:4200` (login `admin`/`Passw0rd!`).
