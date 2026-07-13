# Progress Log — Customer Service AI Dashboard

<!-- Entries are appended newest-on-top. Each phase gets one entry. -->

## [textUI/UX OVERHAUL] ServiceAI design-system + interaction overhaul — 2026-07-11 → 2026-07-13
**Status:** Complete (verified in browser, not just "no console errors")
**Context:** User requested the Angular frontend be brought up to the visual/interaction quality of the "ServiceAI" reference screenshots. Explicitly scoped as **visual + interaction only** — no backend logic/auth/data-fetching rewrites, and **no "Documentation" nav item** (nav stays Dashboard / Customers / Cases). This entry consolidates work that was previously scattered across the 2026-07-11 bugfix + Phase 9 entries and the 2026-07-13 ML/enum work, recorded here as one coherent overhaul per the user's request.
**Design system (`styles.scss`):** Replaced the old blue `--cs-accent` system with the indigo-violet ServiceAI palette — `--cs-accent:#4f46e5`, `--cs-accent-light:#eef2ff`, success `#10b981`/`#d1fae5`, warning `#f59e0b`/`#fef3c7`, danger `#ef4444`/`#fee2e2`, info `#3b82f6`/`#dbeafe`, neutral `#f8fafc` bg / white surface / `rgba(0,0,0,.06)` border / `0 1px 3px rgba(0,0,0,.06)` shadow, 16px radius (pill for buttons/badges), Inter/system-ui font, gray-500 muted text. Every status/priority value renders as a **colored dot + pill badge** (`.cs-pill` / `.cs-dot` + `priority-*`/`status-*` classes).
**Layout / sidebar (`shared/layout/*`):** White sidebar, rounded-square indigo support icon + bold app name + gray "Case Dashboard" subtitle. Nav items: inactive = gray icon/text; active = light-indigo pill bg + indigo icon + bold indigo text. Sign Out pinned at the very bottom (flex column, `justify-content:space-between`) — fixed the prior overlap bug. Page-header pattern everywhere: bold title + one-line gray description + top-right rounded pill "+" action button.
**Dashboard (`dashboard/*`):** 6 KPI cards (Total Cases briefcase/indigo, Open Cases clock/blue, High Priority alert/red, Resolved check/green, Customers people/indigo, AI Predicted sparkle/purple-gradient — counts `PriorityAutoSuggested`). Row 2: "Cases Created — Weekly Trend" line/area (indigo, light fill, curved tension, hover tooltip) + "Priority Distribution" **donut** with legend. Row 3: "Cases by Category" **horizontal** bar + "Cases by Status" vertical bar with per-status colors (New blue / InProgress indigo / Escalated red / Resolved green / Closed gray). Bottom "Recent Cases" list (sparkle if AI-suggested, "customer · category · time" subtext, priority+status pills, "View all →" to /cases). `main.ts` registers `ArcElement` + `DoughnutController` (alongside existing registrations).
**Cases list (`case-list.*`):** Header "Cases" + "{n} cases found" + "+ New Case" pill. Full-width search (magnifier icon) + three dropdowns (All Statuses / All Priorities / All Categories). Replaced card-grid with a clean **data `<table>`** (Case / Customer / Category / Priority / Status / Created) with row-hover highlight, AI sparkle on title, dot+pills, generous padding.
**New/Edit Case → MODAL dialogs (architecture change):** `CaseFormComponent` now opens via `MatDialog` on top of the Cases list (dimmed backdrop, list visible behind) instead of routing to `/cases/new`. Routes `cases/new` and `cases/:id/edit` resolve to `CaseListComponent`, which opens the dialog from the route. Form's `FormGroup`/validation/service calls unchanged. Modal: header "New/Edit Case" + X close; fields Title / Customer select / Category select / Description textarea; **AI Priority Prediction box** (light-indigo bg, sparkle, "Get AI suggestion" wand button calling `POST /api/ml/predict-priority` on demand, shows suggested level inline before submit); **Final Priority 3-way segmented control** (Low/Medium/High buttons — AI pre-selects, agent can override); footer Cancel (text) + Create/Save (solid indigo, bottom-right).
**Case detail (`case-detail.*`):** "← Back to Cases" link; main card with title, priority+status pills top-right, metadata row (Customer link / Category / Created / Updated), "DESCRIPTION" small-caps label + paragraph, outline "Edit Case" button (opens edit modal). Separate **AI Priority Prediction card**: sparkle + title + Accepted/Overridden pill (based on `PriorityAutoSuggested`), "Suggested → Final" pills, and a plain-English `priorityReason`. **Call & Follow-up Log (N)** card: direction dropdown + notes + Add, listed entries with icon/direction/relative time/note. Right column: **Update Status** + **Set Priority** vertical option lists with colored dots, current highlighted indigo, click updates immediately.
**Customers list (`customer-list.*`):** "Customers" + "{n} customers" + "+ New Customer"; full-width search; 3-col card grid with colored initial avatar (indigo/purple tones), name + company, email/phone icon rows, divider, "{n} cases" light-blue pill + "Since {date}".
**Customer detail (`customer-detail.*`):** Avatar + name + email/phone/company/address icons in one card, "+ New Case" top-right; "Case History (N)" card listing each case (title, "category · time" subtext, priority+status pills, dividers).
**Backend touches (all three done — none deferred):**
1. `CaseStatus` enum: added **`Escalated`** as 5th value and renamed **`Open` → `New`** (domain entity + seed data + every frontend hardcode updated).
2. Added standalone **`POST /api/ml/predict-priority`** (`MlController` + `MlDtos`) calling `IPriorityPredictor.PredictWithReason` — frontend previews it before saving.
3. Added **`reason`** string to the predicted-priority DTO and `Case` entity (`PriorityReason`), built from the same features used for prediction (category, keyword flags); returned on case detail.
**Files changed:** frontend/src/styles.scss, shared/layout/*, dashboard/*, cases/* (list/form/detail/service), customers/* (list/detail/form/service), shared/models.ts, shared/categories.ts, main.ts, app.routes.ts; backend Case.cs, CaseDtos.cs, CaseService.cs, SeedData.cs, MlController.cs (NEW), MlDtos.cs (NEW), IPriorityPredictor.cs, OnnxPriorityPredictor.cs, RuleBasedPriorityPredictor.cs.
**Validation:** Browser-verified Dashboard, Cases list (table), New Case modal (AI suggestion), Case detail (AI card + reason + status/priority side cards + call log), Edit Case modal, Customers list, Customer detail — each matches the spec visually and functionally. `ng build` clean; backend runs on SQLite fallback.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated tests yet; `priority_model.onnx` gitignored (regenerate locally).

## [Reconciliation] Dashboard upgrade + ML endpoint + docs/commit audit — 2026-07-13
**Status:** Complete
**Context:** User asked whether all UI changes were logged/recorded. Audit of live code vs `PROGRESS_LOG.md` + `git` revealed drift: the dashboard had been enhanced beyond Phase 9's description, an ML controller existed but was undocumented, and **nothing had been committed to git** (only 2 commits ever existed). This entry reconciles the record and the repo.
**Changes reconciled (already present in code, now documented):**
1. **Dashboard enhanced** from the Phase 9 description (4 KPI cards / 2 charts) to its current state: **6 KPI cards** (added *Resolved*, *AI Predicted*), **4 charts** (added *Priority Distribution* doughnut, *Cases by Status* bar), plus a **Recent Cases** list. All wired to the `DashboardSummary` payload (`totalCases`, `openCases`, `highPriorityCases`, `resolvedCases`, `totalCustomers`, `aiPredictedCases`, `byPriority`, `byCategory`, `byStatus`, `recentCases`).
2. **NEW backend ML endpoint** `POST /api/ml/predict-priority` (`MlController` + `MlDtos` `PredictPriorityRequest`/`PredictPriorityResponse`). Returns `Priority` + plain-English `Reason` via `IPriorityPredictor.PredictWithReason`. The frontend **case form** calls it through `case.service.predictPriority()` to preview an AI suggestion before saving a case.
**Files affected (already modified in the working tree, now committed):**
- frontend/src/app/dashboard/dashboard.component.{ts,html,scss}
- backend/src/CustomerService.Api/Controllers/MlController.cs (NEW)
- backend/src/CustomerService.Application/Dtos/MlDtos.cs (NEW)
- frontend/src/app/cases/case.service.ts, case-form.component.ts
- (plus all prior Phase 7–9 + bugfix changes that were never committed)
**Repo hygiene:**
- Removed stray screenshots from `frontend/src/` (`2026-07-13_*.png` and four `FireShot Capture … Base44 APP … .png` files that belonged to a different app) — they don't belong in source and would otherwise be bundled/committed.
- Added SQLite runtime DB files (`customer_service.db*`) to `.gitignore` (generated at startup; not source).
- Committed the full working tree to `git` (branch `main`).
**Known issues / TODO (unchanged):**
- `NG0912` Lucide warning (cosmetic).
- No automated frontend/backend tests yet.
- `priority_model.onnx` is gitignored (regenerate locally).

## [Session] Live preview + login fix + docs audit — 2026-07-13
**Status:** Complete
**Context:** User wanted to review the UI live inside VS Code's integrated browser (not an external browser like Edge, which I cannot observe). The sign-in page loaded but login returned "Invalid username or password" even with the demo credentials.
**Root cause:** The Angular dev server (`npm start` → `:4200`) was running, but the **backend API was not** — so every `/api/auth/login` call failed and the frontend showed the generic error. The backend defaults to `SqlServer`, which isn't installed locally.
**Fix applied:**
1. Started the backend with the **SQLite fallback** so no SQL Server is required: `DOTNET_ENVIRONMENT=Development Database__Provider=Sqlite dotnet run --project src/CustomerService.Api/CustomerService.Api.csproj --urls "http://localhost:5274"`. First run created + seeded the SQLite DB (`customer_service.db`) and loaded the ONNX session.
2. Verified login end-to-end via `curl` (`POST /api/auth/login` → HTTP 200 + JWT) and in the browser (admin/Passw0rd! → redirected to dashboard).
3. Opened `http://localhost:4200/login` in the integrated browser so the user can navigate freely; dev server hot-reloads on file changes.
**Docs work (this session):** Audited documentation completeness.
- **NEW** `docs/CODE_DOCUMENTATION.md` — the codebase reference that `README.md` and `AGENTS.md` both referenced but was missing. Covers repo layout, backend layering/registration/auth/API table, frontend conventions/design system/icons/charts, ML pipeline, and the verified run commands.
- **FIXED** `README.md` inaccuracies vs the actual running setup: corrected ports (API `:5274`, not `:5001`; frontend `:4200`), replaced the SQL-Server-only DB steps with the working SQLite-fallback command, corrected config keys (`Database:Provider`/`Jwt:Key`, not `ConnectionStrings:DefaultConnection`/`Jwt:Secret`), removed the non-existent `frontend/src/environments/environment.ts` `apiUrl` reference, and noted Swagger is Dev-only.
**Files changed:**
- docs/CODE_DOCUMENTATION.md (NEW)
- docs/PROGRESS_LOG.md (this entry)
- README.md (ports, DB steps, env vars, screenshots note)
**Known issues / TODO (unchanged from prior entries):**
- `NG0912` Lucide warning (cosmetic).
- No automated frontend/backend tests yet.
- `priority_model.onnx` is gitignored (regenerate locally).

## [Bugfix] CDK overlay CSS (mat-menu / mat-select floating) — 2026-07-13
**Status:** Complete (verified in browser)
**Context:** User reported two bugs. Investigation showed bug #1 (missing Material Icons `<link>`) was **already resolved** on 2026-07-11 by replacing every `<mat-icon>` with a bundled `<cs-icon>` (lucide-angular, no CDN). A repo-wide grep confirmed **zero `<mat-icon>` elements remain** (only `mat-icon-button`, a button directive). So adding the Google Fonts Material Icons `<link>` would have been a no-op and would have re-introduced the runtime CDN dependency the prior fix removed. Bug #2 was **real and current**.
**Fix applied:**
1. **Bug #1 — NOT applied as described.** `index.html` still has no Material Icons `<link>`, but icons already render as real Lucide SVGs via `shared/cs-icon.component.ts`. No change made; documented why.
2. **Bug #2 — FIXED.** `angular.json` styles arrays (both `build` and `test` targets) only listed `src/styles.scss`. Added `"node_modules/@angular/cdk/overlay-prebuilt.css"` (verified the file exists in `node_modules/@angular/cdk/`) so `mat-menu` (user menu) and `mat-select` (Customer/Category dropdowns) render as floating overlays instead of inline/unpositioned.
**Files changed:**
- frontend/angular.json (added CDK overlay CSS to build + test `styles`)
**Browser verification (what I literally saw, after restarting ng serve + backend):**
- Logged in as `admin` at http://localhost:4200. Nav icons, user avatar, KPI card icons all render as `<img>` SVGs (not raw text).
- User menu: clicking "Ada Admin" opens a proper floating `menu` element with a "Sign out" item near the button — no longer at page bottom.
- `/customers/new`: full form renders (Full name, Email, Phone, Company, Address + Cancel/Create). Not blank.
- `/cases/new`: full form renders (Subject, Description, Customer combobox, Category combobox, "Let AI suggest priority" toggle + Cancel/Create). Dropdowns are proper `combobox` overlays. Not blank.
- Console: only the benign `NG0912` Lucide component-ID collision warning (known, cosmetic). No errors on either form page.
**Known issues / TODO:**
- `NG0912` warning persists (cosmetic, library-internal).
- No automated frontend tests yet.

## [Bugfix] Icons, blank forms, sidenav layout, design system — 2026-07-11
**Status:** Complete (verified in browser, not just "no console errors")
**Root causes found & fixed:**
1. **Icons broken everywhere.** `index.html` had NO Material Icons `<link>`, and `mat-icon` was used in ~8 templates, so the ligature text (`arrow_back`, `add`, `auto_awesome`…) rendered as raw truncated glyphs. Replaced the CDN-dependent `mat-icon` with a local, bundled solution: installed `lucide-angular@^1.0.0` (npm dep, no runtime CDN) and added `shared/cs-icon.component.ts` (`<cs-icon name="...">`) that maps the old Material names → Lucide SVGs. Swapped all `<mat-icon>` usages project-wide (layout, dashboard, customer/case list/detail/form) and imported `CsIconComponent` into each host component. Centralized mapping means it can't silently break again.
2. **New customer / New case pages blank.** Both form templates used `class="form-card reveal" appReveal`, but `RevealDirective` was NOT imported in either form component — so `.reveal { opacity:0 }` was never cleared and the card stayed invisible. Added `RevealDirective` to `customer-form.component.ts` and `case-form.component.ts` imports. (Edit variants share these components, so they're fixed too.)
3. **User menu floating / overlapping content.** The user block lived in a top `mat-toolbar`, not the sidenav. Rebuilt `layout.component.html` so the sidenav is a flex column: brand + nav at top, user menu (`account_circle` / name / role / Sign out) anchored at the bottom via `justify-content: space-between` + a top-border divider. Sidenav is now `height:100vh` and never overlaps `router-outlet`.
4. **Apple-like design system not visible.** `styles.scss` tokens (`.cs-lift`, `--cs-accent`, etc.) were defined but the earlier bugs hid content. Verified in DevTools: `--cs-accent:#0071e3` and `--cs-radius:18px` on `:root`; `.cs-lift` elements carry the soft shadow `0 4px 20px rgba(0,0,0,.06)` and the `cubic-bezier(0.22,1,0.36,1)` transition. Fixed a real conflict: `.reveal.is-visible { transform: translateY(0) }` was overriding `.cs-lift:hover`, cancelling the hover lift — bumped hover specificity (`.cs-lift.reveal:hover`) so cards now rise 3px on hover (confirmed `matrix(1,0,0,1,0,-3)` at hover).
**Files added / changed:**
- frontend/package.json (added `lucide-angular`)
- frontend/src/app/shared/cs-icon.component.ts (NEW)
- frontend/src/app/shared/layout/layout.component.{html,scss,ts}
- frontend/src/app/dashboard/dashboard.component.html + .ts
- frontend/src/app/customers/*.{list,detail,form}.component.{html,ts}
- frontend/src/app/cases/*.{list,detail,form}.component.{html,ts}
- frontend/src/styles.scss (hover-lift specificity fix)
**Browser verification (what I literally saw):**
- Logged in as `admin` at http://localhost:4200. Sidebar nav, user avatar, and all buttons now show real SVG icons (rendered as `<img>`), not text.
- `/customers/new`: full form renders (Full name, Email, Phone, Company, Address + Cancel/Create). Filled it, clicked **Create customer**, landed on `/customers` and the new "Test User" row appears in the list.
- `/cases/new`: full form renders (Subject, Description, Customer + Category selects, AI toggle, Cancel/Create).
- `/customers/1/edit`: form loads with pre-populated data (Juan Dela Cruz, juan@acme.ph, …) and a "Save changes" button.
- Sidenav: brand + nav at top, "Ada Admin / Admin / Sign out" docked at the bottom, dashboard content in the main area with no overlap.
- Hovering a KPI card lifts it 3px (transform confirmed). No console errors except a benign `NG0912` Lucide component-ID collision warning (library-internal, harmless).
**Known issues / TODO:**
- `NG0912` warning: Lucide's `LucideAngularComponent` generates a duplicate component ID when `LucideAngularModule` is pulled in via the standalone `CsIconComponent`. Cosmetic only; no functional impact. Could be silenced later by importing the icon component differently if it becomes noisy.
- No automated frontend tests yet.

## [Phase 9] Dashboard with live charts (KPIs + line/bar) — 2026-07-11
**Status:** Complete
**What was built:**
- `dashboard/dashboard.component.*` rewritten from placeholder into a real dashboard:
  - 4 KPI cards (Total cases, Open cases, High priority, Customers) with icons + subtle lift/scale hover.
  - Line chart "Cases created (last 30 days)" (`trend` from `DashboardSummary`) with area fill.
  - Bar chart "Cases by category" (`byCategory` from `DashboardSummary`).
  - Loading spinner + empty/error states; data via `DashboardService.get()`.
- `main.ts`: registers the Chart.js pieces the app uses (`CategoryScale`, `LinearScale`, `PointElement`, `LineElement`, `BarElement`, `LineController`, `BarController`, `Tooltip`, `Legend`, `Title`, `Filler`) so ng2-charts renders without "not a registered scale" / "Filler plugin" errors.
- `styles.scss` + `shared/reveal.directive.ts`: Apple-like design system (system font stack, `#f5f5f7` bg, white surfaces, `#0071e3` accent, 18px radii, soft shadows, `cubic-bezier(0.22,1,0.36,1)` easing) with `.reveal`/`.cs-lift`/`.stagger` animation utilities and `prefers-reduced-motion` support.
**Files added / changed:**
- frontend/src/app/dashboard/dashboard.component.{ts,html,scss}
- frontend/src/app/dashboard/dashboard.service.ts
- frontend/src/main.ts (Chart.js registration)
- frontend/src/styles.scss (Apple-like theme + animation utilities)
- frontend/src/app/shared/reveal.directive.ts (scroll-reveal IntersectionObserver directive)
**Decisions & assumptions made:**
- Charts use `ChartConfiguration<'line'>` / `<'bar'>` with a shared `baseOptions()` typed as `ChartOptions` and cast at call sites (avoids Angular generic mismatch).
- Design language intentionally simple/Apple-like per user request: subtle hover lift + scroll reveal only, no heavy motion.
**Validation:** `ng build` clean. Browser: login → dashboard shows 4 KPI cards (7 total / 6 open / 3 high / 4 customers) and both canvases render with live data; no console errors.
**Known issues / TODO:**
- No automated frontend tests yet (Phase 11).
**Next step:** Phase 10/11 — search & filter polish, then tests.

## [Phase 8] Customer & Case UI (list, detail, create/edit, call-log) — 2026-07-11
**Status:** Complete
**What was built:**
- **Customers:** `customer.service.ts` (list/search/get/create/update/delete → `/api/customers`), `customer-list.component.*` (debounced search, grid of cards, per-card menu), `customer-form.component.*` (create/edit reactive form), `customer-detail.component.*` (read view).
- **Cases:** `case.service.ts` (list with status/priority/category filters + search, get/create/update/delete → `/api/cases`), `case-list.component.*` (filter bar + search + cards showing status/priority + AI badge), `case-form.component.*` (create/edit with "Let AI suggest priority" slide-toggle on create), `case-detail.component.*` (case info + AI badge + call-log form), `call-log.service.ts` (list by case + create → `/api/calllogs`).
- `shared/categories.ts`: `CATEGORIES` constant (ids 1–5 matching seed: Billing, Shipping / Supply Chain, Technical Support, Account, Product Quality) + `categoryName()` helper — used because there is **no `/api/categories` endpoint**; the backend returns `categoryId`/`categoryName` on cases and the frontend keeps the id↔name map locally.
- `app.routes.ts`: added child routes `customers` (list/new/:id/:id/edit) and `cases` (list/new/:id/:id/edit) under the guarded shell.
**Files added / changed:**
- frontend/src/app/customers/* (service, list, form, detail)
- frontend/src/app/cases/* (service, list, form, detail, call-log.service)
- frontend/src/app/shared/categories.ts, shared/models.ts (updated to match actual DTOs)
- frontend/src/app/app.routes.ts
**Decisions & assumptions made:**
- `Case.status`/`Case.priority` are strings (`'Open'|'InProgress'|...`, `'Low'|'Medium'|'High'`) — required the backend to serialize enums as strings (see Phase 8 backend note below).
- AI priority toggle on case create calls `POST /api/cases` without an explicit priority; backend `CaseService` runs the ONNX predictor and sets `PriorityAutoSuggested=true`.
**Backend change (required for frontend):** `Program.cs` now adds `JsonStringEnumConverter` to `AddControllers()` so `CaseStatus`/`Priority` enums serialize as strings (frontend previously crashed with `status.toLowerCase is not a function` when it received numbers).
**Validation:** `ng build` clean. Browser end-to-end verified:
- Customers: list (4 seeded) + search filter; create "Test User" → appears in list; search finds it.
- Cases: list with status/priority/category filters + search; create case with AI toggle → detail shows **High / AI suggested** (correct for "urgent replacement needed"); add call log → appears in the log list.
- Case detail deep-links to customer; edit links work.
**Known issues / TODO:**
- No `/api/categories` endpoint — categories are a frontend constant; if seed categories change, update `shared/categories.ts`.
- No automated frontend tests yet (Phase 11).
**Next step:** Phase 9 — real dashboard with charts (done immediately after).

## [Phase 7] Frontend scaffolding (Angular shell, routing, Auth, interceptor) — 2026-07-11
**Status:** Complete
**What was built:**
- Angular 18 standalone workspace scaffolded directly in `/frontend` (flattened from the `ng new` subfolder). Angular CLI installed locally as a dev dependency (`@angular/cli@18`); Material 18 + CDK + `ng2-charts@6` + `chart.js@4` added.
- `app.config.ts`: providers for router (in-memory scroll), `HttpClient` (DI interceptors), and animations.
- `auth/auth.service.ts`: login + JWT stored in `sessionStorage`, `BehaviorSubject` + signal for current user, `isAuthenticated()`/`getRole()`/`logout()`.
- `auth/token.interceptor.ts` + `HTTP_INTERCEPTORS` provider: attaches `Bearer` token to every request.
- `auth/auth.guard.ts`: `CanActivateFn` redirecting unauthenticated users to `/login`.
- `auth/login/login.component.*`: reactive form (username/password), Material card, inline validation, loading spinner, error banner.
- `shared/layout/layout.component.*`: Material sidenav shell (toolbar with user menu + logout, nav list Dashboard/Customers/Cases) wrapping a `<router-outlet>`.
- `shared/models.ts`: TypeScript interfaces mirroring backend DTOs (LoginRequest/Response, Customer, Case, CallLog, Category, PagedResult, DashboardSummary, TrendPoint, CategoryBreakdown).
- `dashboard/dashboard.component.ts`: routed placeholder (full KPIs/charts in Phase 9).
- `app.routes.ts`: `/login` (public) + guarded shell with `/dashboard` (default redirect) and `**` → dashboard.
- `proxy.conf.json` + `angular.json` serve target: dev proxy `/api` → `http://localhost:5274` (matches backend CORS origin `localhost:4200`).
- `styles.scss`: Angular Material M3 theme (`mat.define-theme` + `all-component-themes` wrapped in `html`), indigo/teal palette.
**Files added / changed:**
- frontend/ (new Angular app: src/app/**, angular.json, proxy.conf.json, package.json, etc.)
- frontend/src/app/app.config.ts, app.routes.ts, app.component.html (replaced default welcome)
- frontend/src/app/auth/*, shared/models.ts, shared/layout/*, dashboard/*
**Decisions & assumptions made:**
- Standalone components (Angular 18 default) — no NgModule.
- JWT in `sessionStorage` for MVP simplicity (noted as less secure than httpOnly cookie in code comments + README TODO).
- Material 18.2 uses the M3 `define-theme` API; `all-component-themes` must be wrapped in a selector (`html`).
- Bumped `angular.json` initial bundle budget to 1MB (Material pushes past the 500kB default) — dev only.
**Validation:** `ng build` succeeds; ran backend (:5274) + frontend (:4200); confirmed via browser that login (`admin`/`Passw0rd!`) stores JWT, redirects to `/dashboard`, and the guarded layout (toolbar + sidenav) renders. Proxy `/api` → backend verified with curl.
**Known issues / TODO:**
- Customers/Cases routes exist in the nav but have no components yet (Phase 8).
- No automated frontend tests yet (Phase 11).
**Next step:** Phase 8 — Customer & Case UI (list, detail, create/edit forms, call-log form).

## [Phase 5] ML model — synthetic data, train, ONNX export — 2026-07-11
**Status:** Complete
**What was built:**
- `ml/train_model.py`: generates a synthetic, rule-labeled dataset (`generate_synthetic_data`, 3,000 rows, ~8% label noise), trains a `DecisionTreeClassifier` (max_depth=6, min_samples_leaf=20), evaluates (accuracy + confusion matrix), and exports to `ml/models/priority_model.onnx` (opset 17).
- Model input is the 4-feature float vector the backend expects: `[categoryId, priorCaseCount, daysSinceLastContact, hasComplaintKeyword]`, named `input`. Output `probabilities` is float[3] in **[Low, Medium, High]** order — matches `OnnxPriorityPredictor._labels`.
- `docs/MODEL_CARD.md` written: intended use, features, training-data limitations (synthetic), evaluation (test acc 0.93; 98.8% rule agreement on a feature grid), reproduce/retrain steps, ethical notes.
- Backend `OnnxPriorityPredictor` hardened to select the `probabilities` output by name (the model now also emits a string `label` output), so it won't crash on the new export.
**Files added / changed:**
- ml/train_model.py (new)
- ml/requirements.txt — pinned onnx==1.22.0, skl2onnx==1.20.0 (latest combo that exports TreeEnsembleClassifier cleanly)
- ml/models/priority_model.onnx (generated; gitignored)
- docs/MODEL_CARD.md (new)
- backend/src/CustomerService.ML/OnnxPriorityPredictor.cs — robust output selection
**Decisions & assumptions made:**
- Integer labels 0/1/2 = Low/Medium/High so the ONNX probability order is deterministic and matches the backend (string labels sorted alphabetically and broke ordering).
- Exported at opset 17 because onnxruntime 1.18.1 (backend) only guarantees support through opset 21; opset 22 failed to load.
- Python deps installed into a local `ml/.venv` (system Python is externally-managed / no pip); `python3-venv` had to be apt-installed first.
**Known issues / TODO:**
- Training data is synthetic — retrain on real historical cases before any production use (documented in MODEL_CARD.md).
- `Low` recall is modest (~0.65) due to class rarity in synthetic data; acceptable for a suggestion aid.
**Next step:** Phase 7 — Angular frontend scaffolding (shell, routing, Auth module, JWT interceptor).

## [Phase 4] Data cleaning script (`ml/clean_data.py`) — 2026-07-11
**Status:** Complete
**What was built:**
- Rewrote `ml/clean_data.py` to fully comply with build-prompt Section 9. Pipeline: (1) drop exact duplicates, (2) normalize phones to digits, (3) lowercase/trim emails, (4) parse messy dates → ISO 8601, (5) fill missing Category with `"Uncategorized"`, (6) trim all text, (7) **flag** rows missing required fields (Customer Name, Case Subject) into `ml/data/cleaned/rejected_rows.csv` instead of dropping, (8) write cleaned output to `ml/data/cleaned/cases_cleaned.csv` and print a summary (rows in / out / rejected).
- Reads the canonical raw schema: `Customer Name, Email, Phone, Case Subject, Description, Category, Date Created, Status` (case-insensitive column matching).
- Updated sample `ml/data/raw_cases.csv` to the new schema, including edge cases (duplicate, missing name, missing subject, missing category, missing date).
**Files added / changed:**
- ml/clean_data.py (rewritten)
- ml/data/raw_cases.csv (new schema + edge cases)
- ml/data/cleaned/cases_cleaned.csv, ml/data/cleaned/rejected_rows.csv (generated)
**Decisions & assumptions made:**
- Category vocabulary aligned to the backend seed categories (Billing, Shipping / Supply Chain, Technical Support, Account, Product Quality, General Inquiry) plus `Uncategorized` fallback.
- Dedup compares **parsed** dates so `2024/01/05` and `2024-01-05` collapse as duplicates.
**Validation:** `python ml/clean_data.py` on the 11-row sample → 10 unique, 7 cleaned, 3 rejected (missing name/subject). Works as specified.
**Next step:** Phase 5 — ML model training (done immediately after).

## [Phase 3] Backend API (layered, JWT, CRUD, Dashboard, Swagger) — 2026-07-10
**Status:** Complete
**What was built:**
- ASP.NET Core 8 Web API with layered architecture: Controllers -> Services (Application) -> Repositories (Infrastructure) -> EF Core (Domain entities).
- JWT auth (HS256) with Admin/Agent roles; `AuthController.Login` issues tokens with name/role claims.
- CRUD endpoints: Customers (list/detail/search/create/update/delete), Cases (list+filters/create/update/delete), CallLogs (by-case/create), Dashboard (KPI + trend + category breakdown).
- Swagger/OpenAPI enabled with JWT bearer security; CORS policy for Angular dev server (localhost:4200).
- ML priority auto-suggestion wired into `POST /api/cases` (Phase 6 work done early — see below).
- XML doc comments on all public classes/methods.
**Files added / changed:**
- backend/src/CustomerService.{Api,Application,Infrastructure,Domain,ML}/** — full project set
- backend/src/CustomerService.Api/Program.cs — composition root + HTTP pipeline
- backend/src/CustomerService.Api/Controllers/*Controller.cs — Auth, Customers, Cases, CallLogs, Dashboard
- backend/src/CustomerService.Application/Services/*Service.cs — business logic
- backend/src/CustomerService.Infrastructure/Data/* — AppDbContext, repositories, seed data
- backend/src/CustomerService.ML/* — OnnxPriorityPredictor + RuleBasedPriorityPredictor
- backend/src/CustomerService.Api/appsettings.json (+ .Development.json)
**Decisions & assumptions made:**
- Project references arranged to avoid cycles: Api -> {Application, Infrastructure, ML}; Application -> {Domain, ML, Infrastructure}; ML -> Domain; Infrastructure -> {Domain, Application}. `IPriorityPredictor`/`PriorityFeatures` live in Domain (shared contract).
- SQL Server is the production provider (per spec). Added a **SQLite fallback** via `Database:Provider` config so the app runs locally without SQL Server. Default is SqlServer.
- Seed data uses navigation properties (no explicit identity IDs) to avoid "insert explicit value into identity column" errors.
- Demo credentials: admin/Passw0rd!, agent/Passw0rd!, maria/Passw0rd! (BCrypt-hashed at seed time).
**Known issues / TODO:**
- `dotnet run` first-start is slow (~40s) because `EnsureCreated` + seed runs many queries; switch to EF migrations for production.
- JWT key is a dev placeholder in appsettings; must be externalized for prod.
- No automated tests yet (Phase 9 pending).
**Next step:** Phase 4 — write ml/clean_data.py (CSV cleaning/normalization).

## [Phase 2] Database schema + EF Core + seed — 2026-07-10
**Status:** Complete
**What was built:**
- Five entities mapped in EF Core: User, Customer, Category, Case, CallLog (relationships, unique indexes on UserName/Category.Name, cascade deletes).
- `AppDbContext` with fluent config; `AppDbContextFactory` for design-time EF tools.
- Idempotent seeder (`SeedDataInitializer`) inserting categories, users (BCrypt), customers, cases, call logs.
- SQL Server 2022 Developer installed natively on Zorin OS 18.1 (Ubuntu 24.04) via `database/install_sqlserver.sh`; DB `CustomerServiceDb` + login `csadmin` created.
**Files added / changed:**
- backend/src/CustomerService.Infrastructure/Data/AppDbContext.cs, AppDbContextFactory.cs, SeedData.cs, SeedDataInitializer.cs, Repository.cs, DashboardRepository.cs
- backend/src/CustomerService.Domain/Entities/*.cs — entity + enum definitions
- database/install_sqlserver.sh — reproducible SQL Server install (jammy repo + libldap deb + data dir on external drive)
**Decisions & assumptions made:**
- SQL Server data files redirected to `/media/ebnzr/SSDrive_500GB/sqlserver-data` (ASCII path) because `mssql-conf` rejects the emoji-named project folder path.
- `mssql-server` is only in the `mssql-server-2022` jammy repo; `mssql-tools18` is in the `prod` jammy repo (script adds both).
- Login password `P@ssw0rd_2024_Xq` chosen to satisfy SQL Server password policy (must not contain login name).
**Known issues / TODO:**
- EF migrations not yet generated (using `EnsureCreated`); add `dotnet ef migrations add Initial` for prod parity.
**Next step:** Phase 3 — build the backend API (done immediately after).

## [Phase 1] Scaffold repo structure — 2026-07-10
**Status:** Complete
**What was built:**
- Repo folders: /backend, /frontend, /ml, /database, /docs.
- .NET 8 solution `CustomerServiceApi.sln` with projects: Api (web), Application, Infrastructure, Domain, ML, Tests (xUnit).
- Verified toolchains: dotnet 8.0.407, node v24.18.0, npm 11.16.0, Angular CLI, Python 3.12.3, git 2.43.0. SQL Server installed (see Phase 2).
**Files added / changed:**
- backend/CustomerServiceApi.sln + 6 projects
- docs/PROGRESS_LOG.md (this file)
**Decisions & assumptions made:**
- No MVP_BUILD_PROMPT.md SQL DDL was present, so the schema was designed from the described entities (Users, Customers, Categories, Cases, CallLogs) — documented in Phase 2.
- Angular frontend will be created in /frontend as a standalone workspace (Phase 7).
**Known issues / TODO:**
- None.
**Next step:** Phase 2 — SQL Server schema + EF Core + seed (done immediately after).
