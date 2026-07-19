# Progress Log — Customer Service AI Dashboard

<!-- Entries are appended newest-on-top. Each phase gets one entry. -->

## [Phase 17.3] Bell polish: priority color + consistent days-overdue — 2026-07-19
**Status:** Complete (verified — backend 24/24 tests; browser: priority pills colored High(red)/Medium(amber)/Low(green); bell days-overdue now matches the dashboard exactly, e.g. case 6 = 3, case 2 = 2)
**Context:** Two bell issues: (1) the priority column had no color highlight (the bell used `.item-priority.priority-*` classes but only `.cs-pill.priority-*` was styled, so pills were unstyled); (2) the "days overdue" number in the bell differed from the dashboard list. Root cause of (2): the dashboard computes `daysOverdue` **server-side** via `OverduePolicy.DaysOverdue` (UTC), while the bell recomputed it **client-side** from the UTC timestamp using `new Date(...)` — which the browser interprets in **local** time, shifting the day count (several cases showed "0 d overdue" in the bell vs "3 d" on the dashboard).
**Changes:**
- `backend/src/CustomerService.Application/Dtos/CaseDtos.cs` — added `int? DaysOverdue` to `CaseDto`.
- `backend/src/CustomerService.Application/Services/CaseService.cs` — `ToDto` now sets `DaysOverdue = OverduePolicy.NeedsFollowUp(c) ? OverduePolicy.DaysOverdue(c) : null`, so the cases endpoint and dashboard share the identical server-computed value (no timezone drift).
- `frontend/src/app/shared/models.ts` — added `daysOverdue: number | null` to `Case`.
- `frontend/src/app/shared/notification-state.service.ts` — bell now uses the server `c.daysOverdue` instead of recomputing locally; removed the now-unused `computeDaysOverdue` helper.
- `frontend/src/app/shared/notification-bell.component.scss` — added `.item-priority.priority-high/medium/low` color rules (High = red w/ light-red fill, Medium = amber, Low = green), mirroring the dashboard pill palette.

## [Phase 17.2] Persistent modal notification center (live list, session read) — 2026-07-19
**Status:** Complete (verified — frontend build OK; 13/13 tests; browser: bell shows live "7 cases need follow-up" matching the dashboard KPI, modal lists all overdue cases with expandable detail rows, "Mark all read" hides the badge for the session, logout→login brings the badge back for still-overdue cases)
**Context:** The Phase 17 bell was "useless" — it was built on stored `Notification` rows that were marked permanently `Read` after one click and never returned, even after logout/login. User wanted a genuinely useful center: a modal (like the new-case form) listing every case needing follow-up (title, customer, priority), with click-to-expand detail (category, description, how long it's sat, follow-up log, link to the case), and "Mark all read" that hides the badge for the session but lets notifications **persist** and reappear on next login for cases still overdue.
**Changes:**
- `frontend/src/app/shared/notification-state.service.ts` (new) — `NotificationStateService` drives the center from a **live** `CaseService.list({overdue:true})` call (not stored rows), so a case stays listed for as long as it is overdue. `readDismissed` signal persisted in `sessionStorage` (`cs_read_overdue`); `visibleCount` = 0 when dismissed this session, else the live count. `dismissAll()` hides the badge; `reset()` restores it (called on logout). `loadDetail(caseId)` fetches call logs for the expanded row.
- `frontend/src/app/shared/models.ts` — added `OverdueCase` (caseId, subject, customerName, assignedToUserName, priority, followUpDueUtc, daysOverdue, detail: Case).
- `frontend/src/app/shared/notification-bell.component.ts/.html/.scss` — rewritten as a **modal** (centered panel + backdrop, Apple-like tokens, `prefers-reduced-motion` guarded). Lists overdue cases; each row expands inline to show status/category/assigned/opened/due facts, description, follow-up log, and an "Open in Cases" button (→ `/cases/{id}`). Badge uses `state.visibleCount()`; "Mark all read" → `state.dismissAll()`.
- `frontend/src/app/auth/auth.service.ts` — `logout()` now calls `notifications.reset()` so the session-scoped read state is cleared and the badge returns on next login (the root singleton's in-memory signal otherwise survives the SPA logout/login cycle).
- **Note:** The backend `Notification`/`NotificationsController`/`NotificationService` (Phase 17) are left in place but are no longer used by the frontend; the center is now fully live/computed. `notification.service.ts` (old stored-row client) is now unused and can be removed later.

## [Phase 17.1] Make overdue detection automatic (SLA + stale) — 2026-07-19
**Status:** Complete (verified — backend 24/24 tests; browser shows bell "8 unread" + "8 Overdue Follow-ups" KPI; mark-all clears and stays 0; idempotent)
**Context:** Phase 17 shipped the notification center but the overdue rule only fired when a `FollowUpDueUtc` existed — and nothing ever set it, so only the 2 seed cases were ever flagged. User pointed out open cases with no call logs were ignored. Fix: a single shared `OverduePolicy` so the dashboard, cases filter, and notifications all agree, plus auto-scheduled SLA deadlines on case creation.
**Changes:**
- `backend/src/CustomerService.Domain/OverduePolicy.cs` (new) — single source of truth. `NeedsFollowUp`/`DaysOverdue` flag a case when it is open AND either (a) has a past `FollowUpDueUtc` with no follow-up since, or (b) has no deadline and no follow-up for `StaleDays` (3). `ComputeFollowUpDueUtc` derives an SLA deadline from priority (High=1d, Medium=3d, Low=7d).
- `backend/src/CustomerService.Application/Services/CaseService.cs` — auto-set `FollowUpDueUtc` from SLA on `CreateAsync`; `overdue` filter rewritten to mirror `OverduePolicy` (inline, EF-translatable).
- `backend/src/CustomerService.Application/Services/NotificationService.cs` — `GenerateOverdueAsync` now uses `OverduePolicy.NeedsFollowUp` over in-memory cases (includes CallLogs) and `DaysOverdue` for the message.
- `backend/src/CustomerService.Infrastructure/Repositories/DashboardRepository.cs` — `GetOverdueFollowUpsAsync` rewritten to use `OverduePolicy` (scheduled + stale), so dashboard count matches notifications.
- `backend/tests/CustomerService.Tests/NotificationServiceTests.cs` — added a stale-case assertion (now expects 3: 2 scheduled + 1 stale; resolved still excluded).
- `backend/tests/CustomerService.Tests/DashboardRepositoryTests.cs` — fixed `ExcludesFutureDeadlines` (future deadline + recent creation is not stale) and added `FlagsStaleOpenCaseWithNoDeadline`.
- **Note:** SQLite `EnsureCreated()` — deleted the stale `customer_service.db` so the (unchanged) schema regenerated; seed now yields 8 overdue cases (2 scheduled + 6 stale).

## [Phase 17] In-app notification center for overdue follow-ups — 2026-07-19
**Status:** Complete (verified — backend 23/23 tests; frontend 13/13 tests; browser shows bell with "2 unread" badge, dropdown lists both overdue cases, "Mark all read" clears the badge, reload keeps it cleared; not yet committed)
**Context:** Third roadmap item. Scoped by the user to **"Record + in-app center"**: generate a persisted `Notification` per overdue case and surface it in an in-app bell — no external provider. A pluggable `INotificationSender` seam is left for real Email/SMS later. Generation is idempotent (at most one notification per case, even after it is marked read).
**Changes:**
- `backend/src/CustomerService.Domain/Entities/Notification.cs` (new) — `Notification` entity + `NotificationChannel` (InApp/Email/Sms) + `NotificationStatus` (Unread/Read) enums.
- `backend/src/CustomerService.Infrastructure/Data/AppDbContext.cs` — added `DbSet<Notification>` + mapping (Title/Message required, FK to Case with `SetNull` so notifications survive case deletion).
- `backend/src/CustomerService.Application/Dtos/NotificationDtos.cs` (new) — `NotificationDto` + `NotificationSummaryDto` (unread count + recent).
- `backend/src/CustomerService.Application/Interfaces/INotificationSender.cs` (new) — pluggable delivery contract.
- `backend/src/CustomerService.Application/Interfaces/INotificationService.cs` (new) — generate / list / summary / mark-read / mark-all-read.
- `backend/src/CustomerService.Application/Services/InAppNotificationSender.cs` (new) — persists a `Notification` row (the only sender used by the demo).
- `backend/src/CustomerService.Application/Services/NotificationService.cs` (new) — scans overdue cases (same rule as dashboard), creates one in-app notification per overdue case that does not already have a `Notification` row (read or unread), and serves the list/summary/mark-read.
- `backend/src/CustomerService.Api/Controllers/NotificationsController.cs` (new) — `GET /api/notifications/summary`, `GET /api/notifications`, `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all`. Summary/list trigger generation on demand (no background worker needed for the demo).
- `backend/src/CustomerService.Api/Program.cs` — registered `INotificationSender` + `INotificationService` as scoped.
- `frontend/src/app/shared/models.ts` — added `Notification` + `NotificationSummary` models.
- `frontend/src/app/shared/notification.service.ts` (new) — `NotificationService` (reused name, providedIn root) with `unreadCount` signal + summary/list/markRead/markAllRead.
- `frontend/src/app/shared/notification-bell.component.ts/.html/.scss` (new) — bell button with unread badge, dropdown of recent notifications, "Mark all read", click-to-deep-link to the case. Uses `cs-icon` (`notifications`/`notifications_active`/`schedule`/`inbox`) + design tokens; respects `prefers-reduced-motion`.
- `frontend/src/app/shared/cs-icon.component.ts` — mapped `notifications` → Lucide `Bell` and `notifications_active` → `BellRing`.
- `frontend/src/app/shared/layout/layout.component.html/.ts` — mounted `<app-notification-bell>` in the sidenav brand row; imported the component.
- `backend/tests/CustomerService.Tests/NotificationServiceTests.cs` (new) — 3 tests: one-per-overdue-case generation (resolved case excluded), idempotent de-duplication, and mark-read/mark-all-read lifecycle.
- **Note:** SQLite uses `EnsureCreated()` (no migrations), so the stale `customer_service.db` had to be deleted once to pick up the new `Notifications` table.

## [Phase 16] Revert KPI grid + fully remove overdue chip — 2026-07-19
**Status:** Complete (verified — Cases page shows no Overdue chip, only the toggle; KPI grid reverted to fixed `repeat(7,1fr)` + breakpoints; not yet committed)
**Context:** User rejected the centered/fluid KPI change and pointed out the "Overdue" chip was still rendering (Phase 15 only removed the `clearFilter` branch, not the chip push in `activeChips`, so the chip showed but was unclickable). Reverted both.
**Changes:**
- `frontend/src/app/cases/case-list.component.ts` — removed the `if (f.overdue) chips.push(...)` line from `activeChips` so the Overdue chip is gone entirely (toggle alone controls the filter). The `clearFilter` overdue branch was already removed in Phase 15.
- `frontend/src/app/dashboard/dashboard.component.scss` — reverted `.kpis`/`.kpi-card` back to the original fixed grid (`repeat(7,1fr)` + 4/3/2 breakpoints; `.kpi-card { width:100% }`).

## [Phase 15] KPI polish follow-ups — drop redundant chip + center wrapped cards — 2026-07-19
**Status:** Complete (verified in browser — wrapped KPI rows centered at 1280/980/760/560/420px; Cases page shows Overdue toggle with no chip; not yet committed)
**Context:** User feedback: (a) the "Overdue" removable chip on the Cases page was redundant since the Overdue toggle already shows its active state (toggle off = filter cleared), and (b) KPI cards stretching to fill the row looked bad.
**Changes:**
- `frontend/src/app/cases/case-list.component.ts` — removed the `overdue` chip from `activeChips` and the `overdue` branch from `clearFilter` (toggle alone controls the filter, mirroring `aiOnly`).
- `frontend/src/app/dashboard/dashboard.component.scss` — `.kpis` now `justify-content: center` and `.kpi-card` is `flex: 0 1 170px` (fixed-ish width, no grow) so wrapped rows center instead of stretching edge-to-edge.

## [Phase 14] Overdue KPI → auto-applied "Overdue only" filter — 2026-07-19
**Status:** Complete (verified — backend 20/20 tests; frontend 13/13 tests; browser shows /cases?overdue=true → "2 cases found" with Overdue toggle active + chip; not yet committed)
**Context:** User asked the Overdue Follow-ups KPI card to navigate to the Cases page with an auto-applied "overdue only" filter (matching the existing AI Predicted / status / priority deep-link pattern).
**Changes:**
- `backend/src/CustomerService.Application/Dtos/CaseDtos.cs` — `CaseDto` gains `FollowUpDueUtc` (so the field is available client-side too).
- `backend/src/CustomerService.Application/Services/CaseService.cs` — `GetAllAsync` gains `bool overdue = false` param; when true, filters to open cases (New/InProgress/Escalated) with a past `FollowUpDueUtc` and no `CallLog` since the deadline — the exact rule used by the dashboard. `ToDto` maps `FollowUpDueUtc`.
- `backend/src/CustomerService.Application/Interfaces/ICaseService.cs` — signature updated.
- `backend/src/CustomerService.Api/Controllers/CasesController.cs` — `GET /api/cases` gains `[FromQuery] bool overdue = false`.
- `frontend/src/app/shared/models.ts` — `Case` gains `followUpDueUtc: string | null`.
- `frontend/src/app/cases/case.service.ts` — `list()` sends `overdue=true` when requested.
- `frontend/src/app/dashboard/dashboard.component.ts` — overdue KPI `link` changed to `/cases?overdue=true`.
- `frontend/src/app/cases/case-list.component.ts` — reads `overdue` query param, adds `overdue` to `filters` signal, passes it to `load()`, adds `toggleOverdue()`, and an "Overdue" removable chip.
- `frontend/src/app/cases/case-list.component.html` + `.scss` — new "Overdue" toggle button (amber, mirrors AI Predicted toggle) using `cs-icon name="schedule"`.
- `frontend/src/app/cases/case.service.spec.ts` — sample `Case` updated with `followUpDueUtc: null`.

## [Phase 13] KPI card polish — overdue icon + fluid grid — 2026-07-19
**Status:** Complete (verified in browser at 1280/1100/980/820/700/560/420/360px — every row fills, no empty space; not yet committed)
**Context:** User reported (a) the Overdue Follow-ups KPI card had no icon, and (b) the 7-card KPI grid left empty space on the last row when the screen narrowed.
**Changes:**
- `frontend/src/app/shared/cs-icon.component.ts` — mapped `schedule` → Lucide `AlarmClock` (the overdue card already referenced `icon: 'schedule'`, but it was unmapped so nothing rendered). Imported `AlarmClock` from `lucide-angular/src/icons`.
- `frontend/src/app/dashboard/dashboard.component.scss` — replaced the fixed `grid` (`repeat(7,1fr)` + 3 breakpoints) with a fluid flexbox: `.kpis { display:flex; flex-wrap:wrap; gap:1rem }` and `.kpi-card { flex:1 1 150px; min-width:150px; max-width:100% }`. Cards now stretch to fill every row (including the last partial row), eliminating orphan empty space at any width.

## [Phase 12] Overdue follow-up detection surfaced on the dashboard — 2026-07-19
**Status:** Complete (verified — backend build 0 errors; 20/20 tests pass; frontend build + 13/13 tests pass; browser check shows "2 Overdue Follow-ups" KPI + list; not yet committed)
**Context:** Second item of the README `## Roadmap`, implemented as a deliberately narrow slice per user preference: **detect** overdue follow-ups and **surface** them on the dashboard. No notification-sender abstraction (Email/SMS) was built — outbound delivery remains a separate follow-up item. A follow-up is "overdue" when an open case (New/InProgress/Escalated) has a `FollowUpDueUtc` in the past and has had no call-log follow-up since that deadline.
**Changes:**
- `backend/src/CustomerService.Domain/Entities/Case.cs` — added nullable `DateTime? FollowUpDueUtc` (UTC deadline for the next follow-up).
- `backend/src/CustomerService.Domain/Interfaces/OverdueFollowUpSummary.cs` (new) — lightweight summary DTO (CaseId, Subject, CustomerName, AssignedToUserName, Priority, FollowUpDueUtc, DaysOverdue).
- `backend/src/CustomerService.Domain/Interfaces/IDashboardRepository.cs` — `DashboardSummary` gains `OverdueFollowUps` (int) + `OverdueFollowUpDetails` (List); added `GetOverdueFollowUpsAsync()`.
- `backend/src/CustomerService.Infrastructure/Repositories/DashboardRepository.cs` — implemented `GetOverdueFollowUpsAsync()` (open + past-deadline + no follow-up-since-deadline; sorts most-overdue first) and wired it into `GetSummaryAsync()`.
- `backend/src/CustomerService.Application/Dtos/DashboardDtos.cs` — `DashboardDto` gains `OverdueFollowUps` + `OverdueFollowUpsList` (`OverdueFollowUpDto`).
- `backend/src/CustomerService.Application/Services/DashboardService.cs` — maps summary → DTO.
- `backend/src/CustomerService.Infrastructure/Data/SeedDataInitializer.cs` — seeded `FollowUpDueUtc` (a few days in the past) on two open cases (case 2 "Package not delivered", case 6 "Integration webhook failing") so the feature is visible on first run.
- `frontend/src/app/shared/models.ts` — `Dashboard` gains `overdueFollowUps` + `overdueFollowUpsList` (`OverdueFollowUp`); added `OverdueFollowUp` interface.
- `frontend/src/app/dashboard/dashboard.component.ts` — 7th KPI card "Overdue Follow-ups" (amber tone).
- `frontend/src/app/dashboard/dashboard.component.html` + `.scss` — new "Overdue Follow-ups" card (amber border) listing each overdue case with a "N days overdue" badge, customer, agent, due date, and priority pill; links to the case.
- `backend/tests/CustomerService.Tests/DashboardRepositoryTests.cs` (new) — 5 tests for the overdue rule (open+past, excludes closed, excludes future, excludes followed-up-since-deadline, sorts most-overdue first) using EF Core InMemory. Added `Microsoft.EntityFrameworkCore.InMemory` to the test csproj.
- `frontend/src/app/dashboard/dashboard.component.spec.ts` — sample `Dashboard` updated with the new fields; KPI expectation now 7 cards.
- Docs: `README.md` roadmap updated (sentiment + overdue detection checked; Email/SMS *sending* left as a follow-up).
**Verification:** `dotnet build CustomerServiceApi.sln` → 0 errors. `dotnet test` → 20/20 pass (5 new). `npm run build` → 0 errors (1.08 MB initial, under budget). `npx ng test --watch=false --browsers=ChromeHeadlessCI` → 13/13 pass. Browser (admin): dashboard shows "2 Overdue Follow-ups" KPI and an Overdue Follow-ups list with "3 days overdue" / "2 days overdue" badges. `GET /api/dashboard` returns `overdueFollowUps: 2` with both details.
**Known issues / TODO:** The SQLite dev DB was stale (created before `FollowUpDueUtc` existed) — deleted `backend/src/CustomerService.Api/customer_service.db` so `EnsureCreated()` recreated the schema; reseed regenerates it. Email/SMS *sending* for overdue follow-ups is NOT implemented (detection + dashboard surfacing only). The `.onnx` remains gitignored by design.

## [Phase 11] Frontend unit tests now runnable (system Chrome installed) — 2026-07-19
**Status:** Complete (verified — `ng test` runs, 13/13 specs pass; documented; not yet committed)
**Context:** During Phase 10, `ng test` (Karma) could not run because the only Chrome on this machine was a flatpak sandbox that Karma cannot launch/drive. The user installed the official Google Chrome `.deb` (v150.0.7871.128 at `/usr/bin/google-chrome`), which is a normal system binary Karma can exec directly. This unblocks the frontend test suite.
**Changes:** None to application code. Test command (run from `frontend/`):
```
export CHROME_BIN=$(which google-chrome)
npx ng test --watch=false --browsers=ChromeHeadlessCI
```
The `ChromeHeadlessCI` launcher already exists in `karma.conf.js` with `--no-sandbox` (required when running as root). Note: the flatpak Chrome (`flatpak run com.google.Chrome`) does NOT work for Karma — use the system `.deb` or Puppeteer's Chrome-for-Testing instead.
**Verification:** `npx ng test --watch=false --browsers=ChromeHeadlessCI` → `TOTAL: 13 SUCCESS` (0.621s), including `case.service.spec.ts` which asserts the AI-preview request sends `body.description` and `body.hasComplaintKeyword` is undefined. Frontend coverage of the sentiment change is now confirmed, closing the gap noted in Phase 10.
**Known issues / TODO:** None. (Optional follow-up: add a one-line "Running frontend tests" note to `frontend/README.md` so the `CHROME_BIN` step is discoverable.)

## [Phase 10] Sentiment analysis on complaint text (replaces keyword flags) — 2026-07-19
**Status:** Complete (verified — backend build 0 errors; 15/15 tests pass; model retrained to 0.947 accuracy; frontend tsc 0 errors; browser check shows "Suggested: Medium · ML model" from a description; not yet committed)
**Context:** First item of the README `## Roadmap`: replace the binary `hasComplaintKeyword` flag with a continuous sentiment score derived from the case description. The old feature was a 0/1 switch (keyword present or not); the new one is a lexicon-based score in [-1, 1] (negative = complaint/urgency, positive = satisfaction) so the model sees a graded urgency signal. The scorer is mirrored in Python (`sentiment_score` in `ml/train_model.py`, used for training) and C# (`RuleBasedPriorityPredictor.SentimentScore`, used for inference) — the backend remains the single source of truth; the frontend only sends the raw `description`.
**Changes:**
- `ml/train_model.py` — removed `COMPLAINT_KEYWORDS`/`has_complaint_keyword()`; added `NEGATIVE_LEXICON`/`POSITIVE_LEXICON` and `sentiment_score(text)` returning `(pos-neg)/total` clamped to [-1, 1]. `label_rule` now escalates when `sentiment < -0.1`; synthetic data generates a `sentiment` column; `train()` uses it as the 4th feature. Retrained model → 0.947 test accuracy (was 0.93).
- `backend/src/CustomerService.Domain/Interfaces/IPriorityPredictor.cs` — `PriorityFeatures.HasComplaintKeyword` (bool) → `Sentiment` (float, [-1, 1]).
- `backend/src/CustomerService.ML/RuleBasedPriorityPredictor.cs` — replaced `ComplaintKeywords`/`ContainsComplaintKeyword` with `NegativeLexicon`/`PositiveLexicon` and `public static float SentimentScore(string?)`. Rule escalates on `Sentiment < -0.1` with reason "the description expresses negative/complaint sentiment".
- `backend/src/CustomerService.ML/OnnxPriorityPredictor.cs` — 4th input element is now `features.Sentiment`; reason text updated to match.
- `backend/src/CustomerService.Application/Dtos/MlDtos.cs` — `PredictPriorityRequest.HasComplaintKeyword` (bool) → `Description` (string); backend derives the sentiment score.
- `backend/src/CustomerService.Api/Controllers/MlController.cs` — `PredictPriority` computes `RuleBasedPriorityPredictor.SentimentScore(request.Description)` and builds features with `Sentiment`.
- `backend/src/CustomerService.Application/Services/CaseService.cs` — `CreateAsync` computes sentiment via `SentimentScore` instead of the keyword check.
- `frontend/src/app/cases/case.service.ts` — `predictPriority` sends `{ categoryId, priorCaseCount: 0, daysSinceLastContact: 0, description }` (no keyword array).
- `backend/tests/CustomerService.Tests/PredictorTests.cs` — replaced the keyword test with `RuleBased_SentimentScore_NegativeForComplaints`; updated escalation/neutral/single-signal tests to use `Sentiment` floats.
- `frontend/src/app/cases/case.service.spec.ts` — renamed test asserts `body.description` is sent and `body.hasComplaintKeyword` is undefined.
- Docs: `AGENTS.md`, `docs/MODEL_CARD.md`, `docs/CODE_DOCUMENTATION.md` updated to describe the `sentiment` feature; `ml/train_model.py` docstring fixed.
**Verification:** `dotnet build CustomerServiceApi.sln` → 0 errors/0 warnings. `dotnet test` → 15/15 pass (incl. new `RuleBased_SentimentScore_NegativeForComplaints`). `python ml/train_model.py` → 0.947 accuracy, `ml/models/priority_model.onnx` regenerated. `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. Browser (admin, New Case dialog): typed a complaint description, selected customer+category, clicked "Get AI suggestion" → "Suggested: Medium · **ML model**" badge. (Note: `ng test`/Karma could not run here — the only Chrome is a flatpak sandbox that Karma can't drive; the equivalent logic is covered by the passing C# tests and the tsc type-check.)
**Known issues / TODO:** None. Frontend unit tests need a non-flatpak Chrome to run locally (`npm test`). The `.onnx` remains gitignored by design (regenerate via `ml/train_model.py`).

## [Fix 3] Known-issues TODO: dev servers running for live data — 2026-07-19
**Status:** Complete (verified — backend `:5274` and frontend `:4200` both listening; authed `GET /api/cases` returns 13 seeded rows; `GET /api/dashboard` returns live KPIs; not yet pushed)
**Context:** Third item of the original `### Known issues / TODO` list: "Dev servers (backend `:5274`, frontend `:4200`) must be running for live data." Confirmed both are up (frontend HTTP 200; backend HTTP 401 on unauthed `/api/cases` = auth enforced, then 200 with a valid admin JWT). The earlier missing-model fallback instance was shut down; the live backend (pid 24865) loads the ONNX model from repo `ml/models/`. No code change required — this was a runtime/verification task.
**Changes:** None (verification only). Documented the running state so the TODO list is fully closed.
**Verification:** `ss -ltnp` shows `:4200` (ng serve) and `:5274` (CustomerService) listening. `curl` with admin JWT: `/api/cases` → 13 rows; `/api/dashboard` → `{"totalCases":13,"openCases":11,"highPriorityCases":5,"aiPredictedCases":4,...}`. Frontend `http://localhost:4200` returns HTTP 200.
**Known issues / TODO:** All three original Known-issues items are now resolved (Fix 1 NG0912, Fix 2 ONNX source, Fix 3 dev servers). Next up: README `## Roadmap` items (e.g. sentiment analysis replacing keyword flags).

## [Fix 2] priority_model.onnx: stop silent fallback — resolve model path + surface source in UI — 2026-07-18
**Status:** Complete (verified in browser + API — model now loads from repo `ml/models/` regardless of CWD; `source` field reports "Onnx" vs "RuleBased"; UI shows "ML model" / "rule-based fallback" badge; startup logs a clear warning when missing; backend build 0 errors; 15/15 tests pass; committed locally — `0ae5f5e`, not yet pushed to origin)
**Context:** Known issue: `priority_model.onnx` is gitignored (per AGENTS.md: regenerate, don't commit), and prediction **silently** fell back to rules when absent. Investigation found a deeper bug: `ML:ModelPath` was a relative path (`ml/models/priority_model.onnx`) resolved against the process working directory. When the API runs from `backend/`, it looked in `backend/ml/models/...` (which does not exist) — so even with the model present at repo-root `ml/models/`, the API never found it and always used the silent rule fallback. The fix makes the model discoverable and the fallback explicit/observable.
**Changes:**
- `backend/src/CustomerService.Api/Program.cs` — added `ResolveModelPath(configuredPath, contentRoot)` which tries (in order): the configured path as-is, relative to the content root, and walking up from the content root to the repo root (where `ml/models` lives). The `IPriorityPredictor` singleton now resolves via this helper and logs `LogInformation` ("Priority model loaded from …") when found, or `LogWarning` (with the exact looked-for path and the `ml/train_model.py` remediation step) when missing — so the fallback is never silent. Added `using System.IO;`.
- `backend/src/CustomerService.Domain/Interfaces/IPriorityPredictor.cs` — added `PriorityModelSource` enum (`Onnx` / `RuleBased`) and a `Source` property on `PriorityPredictionResult` (defaults to `RuleBased`).
- `backend/src/CustomerService.ML/OnnxPriorityPredictor.cs` — the ML path now sets `Source = PriorityModelSource.Onnx` (the fallback path already returns `RuleBased`).
- `backend/src/CustomerService.Application/Dtos/MlDtos.cs` — `PredictPriorityResponse` gained a `Source` string (the engine used).
- `backend/src/CustomerService.Api/Controllers/MlController.cs` — response now includes `Source = result.Source.ToString()`.
- `frontend/src/app/cases/case.service.ts` — `predictPriority` response type includes `source: string`.
- `frontend/src/app/cases/case-form.component.ts` — added `suggestedSource` signal, populated from the response.
- `frontend/src/app/cases/case-form.component.html` — the AI result now shows a small badge: "ML model" (purple) when `source === 'Onnx'`, or "rule-based fallback" (amber, with a tooltip explaining how to enable the model) when `source === 'RuleBased'`.
- `frontend/src/app/cases/case-form.component.scss` — added `.ai-source`, `.ai-source--model`, `.ai-source--fallback` styles (pill badges, consistent with the app's calm palette).
**Verification:** Backend `dotnet build` → 0 errors/0 warnings; `dotnet test` → 15/15 pass. API: with the model present, `POST /api/ml/predict-priority` returns `"source":"Onnx"` and the startup log says "Priority model loaded from '/…/ml/models/priority_model.onnx'". With `ML__ModelPath` pointed at a missing file, it returns `"source":"RuleBased"` and logs the warning. In Chrome (logged in as admin, New Case dialog): selecting a customer + category and clicking "Get AI suggestion" shows "Suggested: Medium · **ML model**" (purple badge) when the model is present, and "Suggested: Medium · **rule-based fallback**" (amber badge) when it is absent. The new direct-SVG `cs-icon` also renders correctly inside the dialog.
**Known issues / TODO:** None remaining from the original Known-issues list. Backend/frontend dev servers must be running for live data. The `.onnx` remains gitignored by design (regenerate via `ml/train_model.py`; verified the pipeline runs and exports the model at 0.93 test accuracy).

## [Fix 1] NG0912 Lucide warning: render Lucide SVGs directly (drop i-lucide component) — 2026-07-18
**Status:** Complete (verified in browser — NG0912 gone from console; 15+ Lucide SVGs render correctly on /dashboard with proper viewBox/paths; tsc → 0 errors; committed locally — `0ae5f5e`, not yet pushed to origin)
**Context:** Known issue from prior phases: a cosmetic `NG0912: Component ID generation collision` warning for `LucideAngularComponent` appeared in the browser console. Root cause: `CsIconComponent` imported `LucideAngularModule` (an NgModule declaring `LucideAngularComponent`). Because `CsIconComponent` is itself imported by many standalone components, each importer got its own copy of `LucideAngularComponent`, producing duplicate component IDs. `lucide-angular@1.0.0` does not export `provideLucideIcons`, and `importProvidersFrom(LucideAngularModule)` in `app.config.ts` does not expose the `<i-lucide>` component to templates — so the module had to stay in the component, which kept the collision.
**Changes:**
- `frontend/src/app/shared/cs-icon.component.ts` — removed the `LucideAngularModule` import and the `<i-lucide [img]...>` usage. `CsIconComponent` now renders the icon itself: it builds a standalone `<svg>` string from the `LucideIconData` node array (`[tag, attrs, children?]` tuples) via a small recursive `renderNode`/`renderSvg` helper, sanitizes it with `DomSanitizer.bypassSecurityTrustHtml`, and binds it through `[innerHTML]` on a `<span class="cs-icon-svg">`. Added `DomSanitizer` to the constructor. The `:host ::ng-deep svg` style now sizes the SVG to `1em` so the existing `size` input still scales it (width/height attrs set to `size` px as well). `ICON_MAP` and the Material-ligature name mapping are unchanged.
- `frontend/src/app/app.config.ts` — reverted the temporary `importProvidersFrom(LucideAngularModule)` experiment (no longer needed; the component no longer depends on the Lucide module).
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200`, logged in as admin): console shows **no** NG0912 warning (previously 2 occurrences per load). `page.$$eval('cs-icon svg', ...)` returns 15 SVGs on /dashboard, each `viewBox="0 0 24 24"`, `width="20"`, with correct child `<path>`/`<circle>` counts (e.g. search=2, dashboard=1). Sidebar nav, KPI cards, chart titles, and recent-cases rows all show icons. Login + dashboard + cases navigation all functional with backend on `:5274` (SQLite).
**Known issues / TODO:** `priority_model.onnx` gitignored (Task 2). Backend/frontend dev servers must be running for live data.

## [Phase 9] Customers page: match Cases search-bar styling + align search text/icon — 2026-07-18
**Status:** Complete (verified in browser — Customers search bar now a full-width 76px/20px white card identical to the Cases toolbar; "+ New customer" button rectangular 8px; search text + icon on both pages match the Cases filter-placeholder style; tsc → 0 errors; pushed)
**Context:** Final polish phase. (1) Make the Customers search bar span the full content width inside a parent container, styled exactly like the Cases `SearchFilterToolbarComponent` card. (2) Change "+ New customer" from a pill to a rectangle with ~8px corners (matching Cases "New Case"). (3) Make the two pages look alike for their search icons — same color, same left spacing, same right spacing next to the text. (4) Make the search input text on BOTH pages use the same style as the default placeholder text of the 3 Cases filters (`#64748B`, normal weight 400) for a consistent, calm look.
**Changes:**
- `frontend/src/app/customers/customer-list.component.html` — wrapped the search `mat-form-field` in a new `.search-toolbar` parent container; added `matPrefix` to the `cs-icon name="search"` so Material treats it as a prefix (matching Cases); added `class="new-customer-btn"` to the "+ New customer" button.
- `frontend/src/app/customers/customer-list.component.scss` — added `.search-toolbar` (76px / 20px radius / #E8EDF3 border / white bg / `0 1px 2px rgba(15,23,42,0.04)`) mirroring the Cases toolbar; `.search-field` now 48px, 8px radius, single 1px #DCE6EF border (notched outline hidden); `.prefix-icon` `#94A3B8` with `margin-left:0.85rem` / `margin-right:0.75rem` (matches Cases); search input text `#64748B` / weight 400 (was `#0F172A`/600); placeholder `#64748B`; `.new-customer-btn { border-radius:8px !important; }`.
- `frontend/src/app/cases/search-filter-toolbar/search-filter-toolbar.component.scss` — search input text changed from `#0F172A`/600 to `#64748B`/400 so it matches the dropdown placeholder style (selected dropdown values keep `#0F172A`/600); `.prefix-icon` left margin increased `0.6rem` → `0.85rem` for more breathing room; default filter placeholder color darkened `#94A3B8` → `#64748B` (kept at normal weight 400, not bold).
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome: Customers `.search-toolbar` computes `width:866px` (= content width), `bg:rgb(255,255,255)`, `border:0.8px solid rgb(232,237,243)`, `radius:20px`, `height:76px`; field `height:48px`, wrapper `border:0.8px solid rgb(220,230,239)` @ `8px`; icon `rgb(148,163,184)` (#94A3B8), `margin-left:13.6px` / `margin-right:12px`, renders as SVG. Cases search input + Customers search input both compute `color:rgb(100,116,139)` (#64748B) / `font-weight:400`; Cases filter placeholders also `#64748B`/400 — fully consistent. "+ New customer" button `border-radius:8px`.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored. Backend/frontend dev servers must be running for live data (started this session: backend `:5274`, frontend `:4200`).

## [Phase 8 (revision) — Cases: extract Row A into reusable SearchFilterToolbarComponent + visual fixes — 2026-07-18
**Status:** Complete (verified in browser — toolbar 76px / 20px radius / #E8EDF3 border / white bg; search icon renders as Lucide SVG; single clean 8px control border; placeholders "Status"/"Priority"/"Category"; dropdowns responsive (no truncation); table pills now thin colored outline; filter reset on chip removal works; tsc → 0 errors; pushed)
**Context:** Extract the inline search + 3 dropdowns (Row A) from `CaseListComponent` into a new standalone reusable `SearchFilterToolbarComponent`, then apply a series of visual fixes requested after the initial extraction: (1) the search magnifier was rendering as the literal word "search" because `<mat-icon>` needs the Material Icons webfont (not loaded in this app) — switched to the app's `cs-icon` (Lucide) component; (2) the notched-outline left seams created a "double curve" — replaced with a single border on the wrapper; (3) removing a filter chip did not reset the dropdown to its default because the `toolbar*` input fields were never kept in sync — now synced in both `clearFilter` and the `on*Changed` handlers; (4) added left spacing to the magnifier; (5) changed default placeholder labels to "Status"/"Priority"/"Category"; (6) made the 3 dropdowns responsive (`flex:1 1 160px`) so the full label is readable at full width instead of truncating to "All Stat…"; (7) reduced the control radius from 14px to 8px to match the "+ New Case" button; (8) restyled the table Priority/Status pills from filled backgrounds to a transparent fill with a super-thin 1px outline in the same hue as the text (mirroring the dashboard icon tones).
**Changes:**
- `frontend/src/app/cases/search-filter-toolbar/search-filter-toolbar.component.ts` (NEW) — standalone component; Inputs `statuses`/`priorities`/`categories` + `search`/`status`/`priority`/`category` (query-param pre-fill); Outputs `searchChanged`/`statusChanged`/`priorityChanged`/`categoryChanged`; `FormBuilder` form + `ngOnChanges` patch (no re-emit loop). Swapped `MatIconModule` for `CsIconComponent`.
- `frontend/src/app/cases/search-filter-toolbar/search-filter-toolbar.component.html` (NEW) — `<form class="toolbar">` with search `mat-form-field` (`cs-icon name="search" matPrefix`) + 3 `mat-select` dropdowns (placeholders "Status"/"Priority"/"Category").
- `frontend/src/app/cases/search-filter-toolbar/search-filter-toolbar.component.scss` (NEW) — MD3 toolbar (76px / 20px / #E8EDF3 / white / `0 1px 2px rgba(15,23,42,0.04)`); controls 48px, 8px radius, 1px #DCE6EF border (notched outline hidden, single wrapper border); search `flex:3 1 240px`, dropdowns `flex:1 1 160px`; magnifier `#94A3B8` with left margin; placeholder `#94A3B8`; mobile stacks vertically.
- `frontend/src/app/cases/case-list.component.ts` — imports swapped to `SearchFilterToolbarComponent`; added `statuses`/`priorities`/`categoryNames` + `toolbar*` pre-fill fields; added `onSearchChanged`/`onStatusChanged`/`onPriorityChanged`/`onCategoryChanged` handlers (each also keeps the matching `toolbar*` field in sync); `clearFilter` now resets the matching `toolbar*` field too; `ngOnInit` seeds `toolbar*` from query params (incl. `statuses` now includes `'Open'` so `?status=Open` pre-fills).
- `frontend/src/app/cases/case-list.component.html` — replaced inline `.filters-card` with `<app-search-filter-toolbar>` (wired to handlers + Inputs); kept `.filters-row-2` (AI toggle + chips) as a sibling below.
- `frontend/src/app/cases/case-list.component.scss` — removed dead `.filters-card` rules.
- `frontend/src/styles.scss` — `.cs-pill` priority/status variants changed from filled `*-bg` backgrounds to `transparent` + `1px solid` thin outline in the same hue as the text (e.g. High → `1px solid #fca5a5`, Resolved → `1px solid #6ee7b7`).
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`/cases`): toolbar `height:76px`, `border-radius:20px`, `border:0.8px solid rgb(232,237,243)`, `background:rgb(255,255,255)`; search icon is an SVG (`viewBox 0 0 24 24`, `#94A3B8`) with left gap; control border `0.8px solid rgb(220,230,239)` @ `8px` radius (single curve); dropdowns ~171px wide with full labels; placeholders "Status"/"Priority"/"Category" in `#94A3B8`; `?status=Open` → 10 rows, `?priority=High` → 5 rows; applying then removing a chip resets the dropdown to default; pills render transparent with thin colored outline.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 8 (COMPLETE) — B & C] Cases: AI Predicted toggle row + neutral filter chips; Customer/Category color fix — 2026-07-18
**Status:** Complete (verified in browser — row B: AI Predicted toggle OFF by default (gray/white), ON = purple fill/border/bold; chips neutral gray; ?aiOnly=true deep-link pre-engages toggle + pre-filters; row C: Customer/Category already `rgb(100,116,139)` = Created column; tsc → 0 errors; pushed)
**Context:** Sections B and C of "PHASE 8 (COMPLETE)". (B) Add a second row directly below row A (12px gap, same left alignment) containing an always-visible "✨ AI Predicted" toggle button + removable chips for each active Status/Priority/Category filter. Toggle OFF = white/gray border + muted-purple icon/text; ON = filled light-purple bg, purple border, bold purple icon/text; toggling ANDs with other filters. Chips are neutral gray (purple reserved for the toggle). (C) Customer/Category column text must match the Created column color exactly.
**Changes:**
- `frontend/src/app/cases/case-list.component.html` — moved the active-filter `@for` chip block OUT of `.filters-card` (row A) into a new `.filters-row-2` div directly below it. Added the `.ai-toggle` button (with `auto_awesome` icon + "AI Predicted" label, `[class.active]="filters().aiOnly"`, `(click)="toggleAiOnly()"`, `aria-pressed`) as the first child of row B. Removed the `auto_awesome` icon from chips (AI is now represented by the toggle, not a chip).
- `frontend/src/app/cases/case-list.component.ts` — `activeChips` no longer emits the `aiOnly` chip (the toggle owns that state now); it still emits status/priority/category chips. `toggleAiOnly()` already flips `filters().aiOnly` and reloads; `clearFilter` already resets the right filter. `ngOnInit` already pre-sets `aiOnly` from `?aiOnly=true` (toggle renders ON, list pre-filtered). No logic change needed beyond the chip list.
- `frontend/src/app/cases/case-list.component.scss` — added `.filters-row-2` (flex, `margin-top:12px`, `gap:0.6rem`, wraps). Added `.ai-toggle` (48px, 8px radius, `--cs-border` border, white bg, `--cs-text-muted` text, muted-purple `#8b5cf6` icon; `.active` → `#f3e8ff` bg, `#8b5cf6` border, `#7c3aed` bold text/icon). Replaced the old purple chip styles with neutral-gray `.filter-chip` (white bg, `--cs-border` border, `--cs-text` text, gray `×`, hover → `#f1f5f9`/`#cbd5e1`). Customer/Category already use `class="muted"` (`--cs-text-muted`) from Phase 8 (revised) — no change needed for C.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`/cases`): row B shows only the AI toggle (OFF: `bg:rgb(255,255,255)`, `border:rgba(0,0,0,0.06)`, `color:rgb(100,116,139)`, 48px, 8px radius), 0 chips. Clicking it → ON (`bg:rgb(243,232,255)`, `border:rgb(139,92,246)`, `color:rgb(124,58,237)`, weight 700) and list filtered to 4 AI cases. Setting Priority="High" added a neutral-gray chip (`bg:white`, `border:rgba(0,0,0,0.06)`, `color:rgb(15,23,42)`, gray ×) beside the still-ON toggle; combined filter → 0 cases. Clicking the chip × cleared only Priority (dropdown → "All Priorities", list back to 4 AI cases, toggle still ON). `/cases?aiOnly=true` → toggle already `pressed`/ON, list pre-filtered to 4. Customer/Category cells compute `rgb(100,116,139)` — identical to Created column.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 8 (COMPLETE) — A] Cases filter row: unified search + 3 dropdowns visual design — 2026-07-18
**Status:** Complete (verified in browser — all 4 controls 48px tall, #E2E8F0 1px border, 8px radius; search placeholder + default "All…" gray; chevrons on dropdowns; magnifier on search; no floating labels; no outer card; tsc → 0 errors; pushed)
**Context:** Section A of the "PHASE 8 (COMPLETE)" spec — refine the four filter controls (search + 3 dropdowns) into one flat, visually unified row. Remove the floating `<mat-label>` above each dropdown, give all four an identical height (48px), a single 1px `#E2E8F0` border with 8px radius, and no outer wrapping container. Search: left magnifier icon (gray, vertically centered), gray placeholder "Search by title or customer…". Dropdowns: fixed ~180px width, right-aligned chevron, gray default "All…" value, no label above.
**Changes:**
- `frontend/src/app/cases/case-list.component.html` — removed the three `<mat-label>` elements from the dropdowns. Replaced the empty `<mat-option value="">All …</mat-option>` pattern with `placeholder="All Statuses"` / `"All Priorities"` / `"All Categories"` on each `mat-select` (so the default reads as a gray empty-state placeholder, not a dark selected value). Wrapped the search `<cs-icon name="search" class="prefix-icon">` in `matPrefix` so it sits inside the field, left-aligned.
- `frontend/src/app/cases/case-list.component.scss` — added `::ng-deep` Material overrides scoped to `.filters-card`: all `mat-form-field` height 48px; `--mdc-outlined-text-field-outline-color:#e2e8f0` (hover/focus `#cbd5e1`); `--mdc-outlined-text-field-container-shape:8px`; notch pieces forced to `1px #e2e8f0` border with 8px radius and the notch top border removed (continuous border, no label notch). Search input + select value text set to `0.9rem`; search `::placeholder` and empty-select value text colored `--cs-text-muted` (gray). `.prefix-icon` styled gray, 18px, left margin. `.f-search` flexes (`1 1 auto`); `.f-select` fixed `0 0 180px`. Subscript wrapper hidden.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`/cases`): all 4 controls compute `height:48px`, `border-top-color:rgb(226,232,240)` (#E2E8F0), `border-top-width:0.8px`, `border-radius:8px`. `.filters-card` has `border:0px; box-shadow:none`. Search placeholder computed `rgb(100,116,139)` (gray); magnifier icon present, no chevron. Dropdowns show chevron, no icon, empty-state gray. Selecting "Open" filters correctly and the selected value renders dark `rgb(26,27,31)` (gray reserved for defaults).
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 8 (revised)] Cases filter bar: remove outer card, inline active-filter chips, neutral Customer/Category text — 2026-07-18
**Status:** Complete (verified in browser — no outer wrapping card (border/shadow/padding all 0); active filters render as inline removable chips matching dropdown border/radius; Customer/Category cells now `rgb(100,116,139)` = identical to Created column; tsc → 0 errors; pushed)
**Context:** Styling-only follow-up to Phase 8. (1) The outer bordered/shadowed container around the 4 controls created a "double border" nested look — remove it so the search + 3 dropdowns sit directly on the page background in one flat flex row, each keeping its own single ~1px/8px border. (2) The floating fully-rounded "AI Predicted" pill (active-filter indicator from Dashboard KPI click-through) is replaced by a small removable chip INSIDE the filter row (right after Category), styled like the dropdowns (~8px radius, same border weight) with a "×" that clears that specific filter. (3) Customer/Category column values were rendering in the dark `--cs-text` color (same as Case/Priority/Status), not indigo — but the task asked them to match the Created column exactly, so they now use `--cs-text-muted` (the Created column's color), making them visually indistinguishable from Created.
**Changes:**
- `frontend/src/app/cases/case-list.component.html` — removed `cs-card` from the `.filters-card` wrapper (now a plain flex row). Removed the standalone floating `ai-toggle` button. Added an `@for (chip of activeChips(); track chip.key)` block rendering `.filter-chip` buttons (with `auto_awesome` icon for aiOnly, a label, and a `close` "×" icon) right after the Category dropdown. Added `class="muted"` to the Customer and Category `<td>` cells.
- `frontend/src/app/cases/case-list.component.ts` — added `activeChips` computed that derives chips from the current filter state (status/Open pseudo-status, priority, category name, aiOnly). Added `clearFilter(chip)` which resets just that one filter (status → `isOpenFilter=false` + `status=''`; aiOnly → `aiOnly=false`; else → `''` or `null` for categoryId) and reloads.
- `frontend/src/app/cases/case-list.component.scss` — `.filters-card` is now `display:flex; flex-wrap:wrap; align-items:center; gap:0.85rem; margin-bottom:1.25rem` (no border/shadow/padding). Removed the old `.ai-toggle` pill styles. Added `.filter-chip` (inline-flex, `height:48px`, `border:1px solid var(--cs-border)`, `border-radius:8px`, `--cs-surface` bg, `--cs-text` text, hover → light violet) with `.chip-x` in `--cs-text-muted`. Search/dropdown flex rules retained.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`/cases`): `.filters-card` computed `border:0px none; box-shadow:none; padding:0px`. With `?priority=High&aiOnly=true`: two inline chips ("High", "AI Predicted") with computed `border:0.8px solid; border-radius:8px; height:48px` (matches dropdowns). Clicking a chip's "×" cleared that filter (Priority reset to "All Priorities", list updated to 4 AI-predicted cases) while the other chip stayed. Customer/Category cells computed `rgb(100,116,139)` — identical to the Created column.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 8] Cases page: unified filter card, title weight, CUSTOMER/CATEGORY bug fix, rectangular New Case button — 2026-07-18
**Status:** Complete (verified in browser — CUSTOMER/CATEGORY columns populated; filters in one bordered card with flex-wrap; title weight 500; New Case button radius 8px; tsc → 0 errors; backend builds; pushed)
**Context:** Polish the Cases list. Items: (1) unify search + 3 dropdowns + AI toggle into one responsive bordered card; (2) reduce case-title weight to medium; (3) REAL BUG — CUSTOMER and CATEGORY columns were blank for every row; (4) change "+ New Case" from pill to rectangular (~8px radius) per design spec.
**Root cause of the blank columns:** `CaseService.GetAllAsync` (and `GetByIdAsync`) called `_cases.Query()` which is `AsNoTracking()` with **no `Include`** for the `Customer`/`Category` navigation properties. EF Core has no lazy loading here, so `c.Customer`/`c.Category` were null and `ToDto` emitted empty `CustomerName`/`CategoryName`. The frontend already bound `c.customerName`/`c.categoryName` correctly — the data was simply never populated (this also affected the case detail page).
**Changes:**
- `backend/src/CustomerService.Application/Services/CaseService.cs` — `GetAllAsync` and `GetByIdAsync` now `.Include(c => c.Customer).Include(c => c.Category)` on the query (typed `IQueryable<Case>` to satisfy the `IIncludableQueryable` → `IQueryable` reassignment). This populates `CustomerName`/`CategoryName` (and `AssignedToUserName`) server-side — consistent with how the codebase already resolves related names in DTOs. No frontend lookup map needed.
- `frontend/src/app/cases/case-list.component.html` — wrapped the search input + 3 `mat-select` dropdowns + AI toggle in a single `<div class="filters-card cs-card">`; renamed the New Case button class `pill-btn` → `new-case-btn`.
- `frontend/src/app/cases/case-list.component.scss` — `.filters-card` is `display:flex; flex-wrap:wrap; gap:0.75rem; padding:0.85rem 1rem`. Search `flex:1 1 240px; min-width:200px`; dropdowns `flex:0 1 180px; min-width:150px`; AI toggle `flex:0 0 auto`. `.cell-title` font-weight `600` → `500`. Added `.new-case-btn { border-radius: 8px !important; }`.
- `frontend/src/styles.scss` — removed the now-unused `.pill-btn { border-radius: var(--cs-radius-pill) !important; }` rule.
**Verification:** Backend `dotnet build` → 0 errors. `curl /api/cases` now returns e.g. `"customerName":"Liza Lopez","categoryName":"Account"`. Frontend `tsc --noEmit` → 0 errors. In Chrome (`/cases`): CUSTOMER/CATEGORY columns show names; filters in one bordered card (computed `display:flex; flex-wrap:wrap; border:0.8px solid; radius:16px`); title weight computed `500`; New Case button computed `border-radius:8px`.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 7.1] Bar charts: thickness tweak (barPercentage 0.7, categoryPercentage 0.85) — 2026-07-18
**Status:** Complete (verified — served bundle contains `barPercentage: 0.7` & `categoryPercentage: 0.85` on both bar datasets; tsc → 0 errors; pushed `d5ead0c`)
**Context:** After Phase 7 set `0.6 / 0.8` (and a brief `0.8 / 0.9` trial), the bars looked too thin. Bump thickness slightly while keeping visible padding.
**Changes:** `frontend/src/app/dashboard/dashboard.component.ts` — both bar datasets (`Cases by Category` and `Cases by Status`) updated to `barPercentage: 0.7` and `categoryPercentage: 0.85`. `borderRadius: 6` retained on both. No HTML/SCSS changes.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. `curl http://localhost:4200/main.js` confirms `barPercentage: 0.7` and `categoryPercentage: 0.85` appear twice. Dev server hot-reloaded cleanly.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 7] Bar charts: bar spacing (barPercentage/categoryPercentage) + rounded Status corners — 2026-07-18
**Status:** Complete (verified — served bundle contains `barPercentage: 0.6` & `categoryPercentage: 0.8` on both bar datasets; Status dataset also has `borderRadius: 6`; tsc → 0 errors; dev server rebuilt cleanly)
**Context:** The "Cases by Category" and "Cases by Status" bars stretched edge-to-edge with no breathing room. Add visible padding around each bar, and round the corners of the Status bars.
**Changes:** `frontend/src/app/dashboard/dashboard.component.ts` — both bar datasets gained dataset-level `barPercentage: 0.6` and `categoryPercentage: 0.8` (each bar now occupies ~48% of its category band, leaving clear padding). The "Cases by Status" dataset additionally got `borderRadius: 6` (Chart.js v4 native on bar datasets). The "Cases by Category" dataset already had `borderRadius: 6` from earlier work, so both now have rounded corners; the spacing options are the Phase 7 addition. No HTML/SCSS changes.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. `curl http://localhost:4200/main.js` confirms `barPercentage: 0.6` and `categoryPercentage: 0.8` appear twice (category + status datasets) and `borderRadius: 6` is present on the status dataset. Dev server hot-reloaded with no compile errors. In Chrome (`http://localhost:4200/dashboard`): both bar charts render with padded bars; Status bars have rounded corners.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 6 — revert] Removed the square container (item 1); kept circular legend + count labels — 2026-07-18
**Status:** Reverted (verified in browser — donut wrapper is back to its previous responsive 257×240, no longer a square; legend swatches still circles; labels still show live counts)
**Context:** The user reported the square container made the chart unresponsive/unappealing and asked to undo only item 1 (the `aspect-ratio: 1 / 1` square). Items 2 (circular swatches) and 3 (count labels) were kept.
**Changes (undo of item 1 only):**
- `frontend/src/app/dashboard/dashboard.component.scss`: reverted `.chart-box.donut` to `height: 240px;` (removed `height: auto`, `aspect-ratio: 1 / 1`, the `.donut-card { align-self: start }` rule, and the `.chart-box.donut canvas { width/height: 100% !important }` override that were all added to support the square).
- `frontend/src/app/dashboard/dashboard.component.html`: removed the `donut-card` class from the Priority card (back to `chart-card reveal`).
- **Kept:** `doughnutOptions` still has `usePointStyle: true`, `pointStyle: 'circle'`, and the `generateLabels` count callback (items 2 & 3 from the original Phase 6).
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200/dashboard`): donut wrapper measured 257×240 (fluid width, fixed 240px height — its pre-Phase-6 state); legend config (`usePointStyle`, `pointStyle: 'circle'`, `generateLabels`) still present in source. Dev server hot-reloaded cleanly.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 5 — tweak] Weekly Trend x-axis: show Sundays, Tuesdays & Fridays — 2026-07-17
**Status:** Complete (verified in browser — axis shows date labels on Sun/Tue/Fri, e.g. "Jun 26", "Jun 28", "Jun 30", "Jul 10", "Jul 12", "Jul 14")
**Context:** User wanted more date labels on the Weekly Trend x-axis — specifically Sundays, Tuesdays and Fridays (not just Sundays).
**Changes:**
- `frontend/src/app/dashboard/dashboard.component.ts` (`lineOptions` → `scales.x.ticks.callback`): replaced the single `d.getDay() === 0` check with a `showDays = [0, 2, 5]` allow-list (Sun=0, Tue=2, Fri=5); a tick label is shown only when the parsed date's day-of-week is in that list, otherwise `''`. Tooltip still shows the full date for every point.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome: trend chart x-axis renders date labels on Sundays, Tuesdays and Fridays; non-matching days are blank. (Temporary `window.__trendChart` debug hook used for verification removed before commit.)
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 5 — fix] Weekly Trend x-axis showed 0–29 instead of dates — 2026-07-17
**Status:** Fixed (verified in browser — axis now shows only Sunday date labels, e.g. "Jun 28", "Jul 12"; no more numeric indices)
**Context:** After Phase 5 the trend chart's x-axis rendered the raw data indices `0–29` instead of dates. Root cause: on a Chart.js **category** (line) axis the `ticks.callback` receives the data **index** as `value`, not the label string, so `parseDate(String(value))` returned `null` and the code fell back to returning the number.
**Changes:**
- `frontend/src/app/dashboard/dashboard.component.ts` (`lineOptions` → `scales.x.ticks.callback`): rewrote the callback as a regular `function` (so Chart.js binds `this` to the **scale**) and call `this.getLabelForValue(value)` to resolve the real date label before parsing. Date helpers `parseDate`/`fmtShort`/`fmtLong` made `static` (called as `DashboardComponent.parseDate(...)`) so they work inside the callback regardless of `this`. The Sunday-only rule (`getDay() === 0`) and the full-date tooltip (`callbacks.title`) are unchanged.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome: trend chart has 30 date labels; x-axis ticks show only the Sundays in range ("Jun 28", "Jul 12") — no numeric `0–29` indices. (A temporary `window.__trendChart` debug hook used for verification was removed before commit.)
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 5] Weekly Trend chart polish — 2026-07-17
**Status:** Complete (verified in browser — indigo trending-up icon, thinner line, vertical gradient fill, wider card, Sunday-only axis ticks with full-date tooltips)
**Context:** Polish the "Cases Created — Weekly Trend" chart: add a trending-up icon to its title, thin the line, replace the flat fill with a vertical indigo→transparent gradient, make the trend card visibly wider than the Priority card in the same row, and show x-axis tick labels only for Sundays while keeping full dates in the tooltip.
**Changes:**
- `frontend/src/app/shared/cs-icon.component.ts`: imported `TrendingUp` from `lucide-angular` and mapped the Material-style name `trending_up` → `TrendingUp` (the app uses bundled Lucide SVGs via `CsIconComponent`, not the Material Icons webfont, so `mat-icon` would render nothing).
- `frontend/src/app/dashboard/dashboard.component.html`:
  - First `.charts-row` gained class `trend-row`; its title became `<h2 class="chart-title trend-title"><cs-icon name="trending_up" class="trend-icon"></cs-icon> Cases Created — Weekly Trend</h2>`.
  - Trend labels now use the full `t.date` (was `t.date.slice(5)`) so the tooltip can show the complete date.
- `frontend/src/app/dashboard/dashboard.component.ts`:
  - `trendChart` dataset: `borderWidth` set to `1.5` (thinner line); `backgroundColor` is now a `ctx`-based `createLinearGradient(0, chartArea.top, 0, chartArea.bottom)` with stops `rgba(79,70,229,0.4)` at top → `rgba(79,70,229,0)` at bottom (falls back to a flat color before `chartArea` exists).
  - `lineOptions()`: x-axis `ticks.callback` returns the short label (`Jul 13`) only when the parsed date's day-of-week is Sunday (0), else `''`; tooltip `callbacks.title` parses the label and returns the long form (`Jul 13, 2026`) for every point regardless of the tick rule. Added private helpers `parseDate`, `fmtShort`, `fmtLong`.
- `frontend/src/app/dashboard/dashboard.component.scss`:
  - `.trend-row { grid-template-columns: 1.8fr 1fr; }` (the second `.charts-row` keeps the even split) — trend card renders ~526px vs priority ~292px at desktop width; single-column stack below 900px unchanged.
  - `.trend-title { display: flex; align-items: center; gap: 0.5rem; }` and `.trend-icon { color: #4f46e5; }` (indigo).
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200/dashboard`, login `admin`/`Passw0rd!`): trend card is wider than the Priority card; indigo trending-up icon sits left of the title; line is thin; fill is a vertical gradient; x-axis shows only the 4 Sundays in the 30-day window (`2026-06-21`, `06-28`, `07-05`, `07-12`) while hovering any point shows the full date in the tooltip.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 4] Dashboard charts: zoom/overflow fix + clickable charts — 2026-07-17
**Status:** Complete (verified in browser — no overlap at 150% zoom; all four charts navigate to the matching filtered list)
**Context:** Two dashboard chart improvements: (1) at 150% browser zoom the four chart cards overlapped because grid children couldn't shrink below the canvas's natural size; (2) make each chart clickable, deep-linking to `/cases` with the same filter mapping as the Phase 3 KPI cards.
**Changes:**
- `frontend/src/app/dashboard/dashboard.component.scss`:
  - `.charts-row` changed from `grid-template-columns: 1fr 1fr` → `minmax(0, 1fr) minmax(0, 1fr)` so cards can shrink below the canvas width at high zoom (single-column stack below 900px kept).
  - Added `min-width: 0; overflow: hidden` to `.chart-card`; `.chart-box` gained `width: 100%; min-width: 0` with explicit heights (280px / donut 240px) so Chart.js sizes the canvas correctly.
  - Added a subtle hover lift on `.chart-card` (`.cs-lift`-style transform + `--cs-shadow-hover`) for consistency with the KPI cards.
- `frontend/src/app/dashboard/dashboard.component.html`: added `(chartClick)="onChartClick('trend'|'priority'|'category'|'status', $event)"` to each `<canvas baseChart>`.
- `frontend/src/app/dashboard/dashboard.component.ts`:
  - Added `onChartClick(which, event)` handler: reads `event.active[0].index`, maps it back to the chart's label, and `router.navigate(['/cases'], { queryParams })`. Mapping: Status → `status`; Priority donut → `priority`; Category → `categoryId` (mapped from the category name via the shared `CATEGORIES` constant); Weekly Trend → unfiltered `/cases` (no per-day filtering yet, per spec). Clicks on empty chart area (no `active` element) are ignored.
  - Imported `CATEGORIES` from `../shared/categories`.
- `frontend/src/app/shared/categories.ts`: **synced the `CATEGORIES` constant to the backend seed names** (`Billing`, `Shipping`, `Technical`, `Account`, `Product`) — the dashboard category chart labels come from the backend `Category.Name`, so the previous display names (`Shipping / Supply Chain`, etc.) broke the name→id mapping. This also makes the case-list category dropdown match the seed.
- `frontend/src/app/cases/case-list.component.ts`: `ngOnInit` now also reads `categoryId` from `queryParamMap` and sets `filters.categoryId` (numeric), so chart deep-links with `categoryId` pre-apply the category filter.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200/dashboard`, login `admin`/`Passw0rd!`): at **150% zoom** the four cards lay out in a clean 2×2 grid with no overlap and all canvases keep proper dimensions; clicking Status → `/cases?status=New` (3 found), Priority → `/cases?priority=High`, Category → `/cases?categoryId=3` (Technical, 3 found), Trend → unfiltered `/cases`.
**Follow-up fix (commit `d02abbc`):** Resolved an `NG5` strict-template type error on the `chartClick` binding — the handler param was retyped to `{ event?: ChartEvent; active?: any[] }` (matching ng2-charts' emit shape) and `ChartEvent` was imported from `chart.js`. Verified via `npx ng build --configuration development` → 0 errors.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 3] Dashboard: subtitle + 6 KPI cards (tinted icons, hover, clickable, entrance) — 2026-07-17
**Status:** Complete (verified in browser — subtitle, tinted icons, clickable cards with matching filters, staggered entrance)
**Context:** Polish the dashboard KPI row: exact subtitle, vibrant icon on light tinted bg (not solid dark tile), hover lift, clickable cards that deep-link to the matching filtered list, and a staggered fade+rise entrance.
**Changes:**
- `frontend/src/app/dashboard/dashboard.component.html`:
  - Subtitle changed to exactly "Overview of customer service operations and AI-assisted case management".
  - KPI cards are now `<button class="kpi-card cs-lift" appReveal [class]="'tone-'+k.tone" (click)="openKpi(k.link)">` inside the existing `.kpis.stagger` container (kept `appReveal` + `.stagger` for the entrance animation).
- `frontend/src/app/dashboard/dashboard.component.ts`:
  - Added `Router` inject + `openKpi(link)` → `router.navigateByUrl(link)`.
  - `kpis` getter now carries a `link` per card: Total Cases → `/cases`; Open Cases → `/cases?status=Open`; High Priority → `/cases?priority=High`; Resolved → `/cases?status=Resolved`; Customers → `/customers`; AI Predicted → `/cases?aiOnly=true`.
- `frontend/src/app/dashboard/dashboard.component.scss`: replaced the solid-color `.kpi-icon` tiles with **vibrant icon on light tinted rounded-square bg** per tone — indigo (`#eef2ff`/`#4f46e5`), blue (`#dbeafe`/`#3b82f6`), red (`#fee2e2`/`#ef4444`), green (`#d1fae5`/`#10b981`), purple (`#f3e8ff`/`#8b5cf6`). `.kpi` (mat-card) → `.kpi-card` button (border + surface + shadow, `cursor:pointer`, keeps `.cs-lift` hover lift).
- `frontend/src/app/cases/case-list.component.ts`:
  - `ngOnInit` now reads `queryParamMap`: `status`/`priority`/`aiOnly`. **"Open" is a pseudo-status** (backend defines `OpenCases = total - closed`, i.e. everything except `Closed`) handled via a new `isOpenFilter` signal + client-side filter; `priority`/`status` (real) set the filter signal; `aiOnly=true` sets `filters.aiOnly`.
  - `load()` applies `isOpenFilter` (drop `Closed`) and `aiOnly` (keep `priorityAutoSuggested`) client-side after fetch (dataset is tiny — 13 cases — so client-side filtering is correct here).
  - `updateFilter('status', 'Open')` sets `isOpenFilter` instead of a server status; added `toggleAiOnly()`.
  - `filters` signal gained `aiOnly: false`.
- `frontend/src/app/cases/case-list.component.html`: added an "Open" option to the status `<mat-select>` (value-bound to `isOpenFilter() ? 'Open' : filters().status`) and an "AI Predicted" pill toggle button (`.ai-toggle`, `[class.active]="filters().aiOnly"`, calls `toggleAiOnly()`) so the UI reflects the active deep-link filters.
- `frontend/src/app/cases/case-list.component.scss`: added `.ai-toggle` (indigo/purple pill, active = `#f3e8ff` bg + `#8b5cf6` border/text) matching the design system.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome: subtitle exact; KPI icons are vibrant-on-tint (e.g. indigo `rgb(79,70,229)` on `rgb(238,242,255)`); clicking each card navigates and the list count + filter UI match the KPI — Open→11 (status="Open", no Closed rows), High Priority→5 (priority="High"), Resolved→2 (status="Resolved"), AI Predicted→4 (AI toggle active), Total Cases→13, Customers→/customers. Cards carry `stagger`+`cs-lift`+`reveal` for the entrance animation.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored.

## [Phase 2] Sidebar: persistent active state + collapse toggle + auto-hide — 2026-07-17
**Status:** Complete (verified in browser — active pill persists, collapse + auto-hide work)
**Context:** Three sidebar improvements: (1) the active nav highlight disappeared after clicking because `RouterLinkActive` was never imported, so `routerLinkActive="active"` was silently ignored (the `active` class was never applied); (2) add a collapse/expand toggle; (3) auto-hide on narrow screens.
**Changes:**
- `frontend/src/app/shared/layout/layout.component.ts`:
  - **Bug fix:** added `RouterLinkActive` to the `import` statement **and** the component `imports` array — this is what makes `routerLinkActive="active"` actually apply the `active` class (root cause of the missing highlight).
  - Added `BreakpointObserver` (`@angular/cdk/layout`) + `takeUntilDestroyed`. New signals: `isHandset` (true <768px) and `opened` (sidenav open state, default true). Constructor seeds state from `matchMedia('(max-width: 767px)')` and subscribes to the breakpoint so resizing across 768px flips `mode` (`side`↔`over`) and `opened` (open↔closed) automatically. New `toggleSidenav()` flips `opened`.
- `frontend/src/app/shared/layout/layout.component.html`:
  - Sidenav now binds `[mode]="isHandset() ? 'over' : 'side'"` and `[opened]="opened()"` (was static `mode="side" opened`).
  - Added a collapse/expand icon button to the **right of "ServiceAI"** in the header (`chevron_left` when open, `menu` when collapsed) calling `toggleSidenav()`.
  - Nav links keep `routerLinkActive="active"`; added `(click)="isHandset() && toggleSidenav()"` so tapping a link closes the overlay on mobile.
  - Added a **floating reopen button** (`menu` icon, fixed top-left) inside `<mat-sidenav-content>` shown only when `!opened()`, so the toggle stays reachable when the sidebar is fully hidden.
- `frontend/src/app/shared/layout/layout.component.scss`:
  - `.nav-item:hover` → `.nav-item:not(.active):hover` so hover only affects non-active items; `.nav-item.active` (light-indigo pill + bold indigo) now persists independently of hover.
  - Added `.collapse-btn` (right of brand) and `.floating-toggle` (fixed, shadowed, hover lift) styles.
  - `.content` gained a `padding` transition; `.content.sidebar-closed` shifts `padding-left` to `4.5rem` so the floating button never overlaps the page header.
- `frontend/src/app/shared/cs-icon.component.ts`: added `chevron_left` (ChevronLeft) and `menu` (Menu) Lucide icons to `ICON_MAP`.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200`, login `admin`/`Passw0rd!`): active pill stays on the current route (Dashboard/Customers/Cases) and moves on click; collapse button hides the sidebar and shows the floating reopen button; narrowing below 768px switches to overlay mode + starts closed, reopenable via the same button.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic, unchanged). `priority_model.onnx` gitignored.

## [Phase 1] Login page restyle (Apple-like, design-system aligned) — 2026-07-17
**Status:** Complete (verified in browser — centered white card, indigo logo block, solid indigo pill submit)
**Context:** User wanted the login page to match the app's Apple-like design system instead of the default Material card on a dark blue gradient. Iterated on feedback: (1) card was indistinguishable from the light background → strengthened the shadow; (2) card/elements felt too small → enlarged them.
**Changes (`frontend/src/app/auth/login/`):**
- `login.component.ts`: imported `CsIconComponent` and added it to `imports` so the headset logo renders.
- `login.component.html`: replaced the default `mat-card-header` (title/subtitle) with a centered logo block reusing the sidebar brand structure — indigo gradient tile with the `headset` icon, then "ServiceAI" (bold) + "Case Dashboard" (muted), stacked and centered. Added `submit-btn` class to the Sign-in button.
- `login.component.scss`:
  - `.login-wrapper` background changed from dark blue gradient to `--cs-bg` (light gray).
  - `.login-card` now uses `--cs-surface` (white) + `--cs-radius` (16px) + a stronger neutral shadow (`0 12px 32px rgba(15,23,42,0.12)`) + `--cs-border`, so it clearly floats above the background.
  - Added `.brand-block` / `.brand-logo` / `.brand-text` / `.brand-name` / `.brand-sub` reusing existing tokens (`--cs-accent-gradient`, `--cs-text`, `--cs-text-muted`) to match the sidebar logo.
  - `.submit-btn` is solid indigo with pill radius (`--cs-radius-pill`) and medium weight — same as "Create Case".
  - Error banner uses `--cs-danger-bg`; inputs keep Material `appearance="outline"`.
  - Enlarged for comfort: card `max-width` 380→**440px** + more padding; logo tile 48→**60px** (icon 26→**32px**); brand name 1.25→**1.5rem**, subtitle 0.8→**0.95rem**; form gap 0.75→**1rem** with larger input fields; submit button bigger (`0.7rem 1.5rem`, `1rem` font); error/hint text slightly larger.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200/login`, login `admin`/`Passw0rd!`): light gray background, centered white card with soft shadow, indigo headset logo + "ServiceAI / Case Dashboard", outlined inputs, solid indigo pill "Sign in". Login still works.
**Known issues / TODO:** None.

## [Phase 0] Global route-change loading indicator (per-page spinner) — 2026-07-17
**Status:** Complete (verified in browser — spinner appears on every navigation)
**Context:** User wanted a loading indicator during route navigation so the app doesn't feel frozen. First attempt was a thin indigo top progress bar, but routes are eager (not lazy) so navigation finished faster than the 150ms delay and the bar never showed; a full-page blur overlay was also rejected as covering the whole page. Final approach: a **centered circle spinner that appears in each page's content area** (below the header / search / filters), shown on **every** navigation — not just first load — with no page blur.
**Changes:**
- New `frontend/src/app/shared/route-loading.service.ts`: root `RouteLoadingService` exposing a `loading` signal driven by `router.events` (shows on `NavigationStart`, hides on `NavigationEnd/Cancel/Error`) with a 350ms minimum display so fast eager routes stay perceptible.
- `frontend/src/app/dashboard/dashboard.component.ts`: `loading` is now `computed(() => dataLoading() || routeLoading.loading())`; the existing spinner (below the "Dashboard / Support overview…" header) shows on every navigation.
- `frontend/src/app/customers/customer-list.component.ts`: `loading` is now `computed(() => dataLoading() || routeLoading.loading())`; spinner shows below the search bar on every navigation.
- `frontend/src/app/cases/case-list.component.ts`: same computed; spinner shows below the search bar + filters on every navigation.
- Each page keeps its own internal `dataLoading` signal for the actual fetch, so the spinner reflects both "navigating" and "fetching". The `LayoutComponent` overlay/blur was removed entirely.
**Verification:** `npx tsc --noEmit -p tsconfig.app.json` → 0 errors. In Chrome (`http://localhost:4200`, login `admin`/`Passw0rd!`): clicking between Dashboard / Customers / Cases shows the circle spinner in each page's content area on every switch, with no page blur.
**Known issues / TODO:** None.

## [Tests] Frontend specs now run green (13/13) via flatpak Chrome — 2026-07-15
**Status:** Complete (frontend `ng test` → **13 passed**; backend `dotnet test` → **15 passed**)
**Context:** User wanted the previously compile-only frontend specs actually executed. This machine had no Chrome, so `ng test` could not run. Installed Google Chrome via Flatpak (`flatpak install flathub com.google.Chrome`) and pointed Karma at it with `CHROME_BIN=/var/lib/flatpak/exports/bin/com.google.Chrome` using the existing `ChromeHeadlessCI` launcher (`--no-sandbox --disable-gpu`). Running the specs surfaced 3 real spec-wiring bugs (not app bugs), which were fixed.
**Changes (spec files only — no app code changed):**
- `frontend/src/app/dashboard/dashboard.component.spec.ts`: replaced the mixed `HttpClientTestingModule` import + `provideHttpClient()` (which let the real `HttpClient` win, so the mock never saw `/api/dashboard`) with `provideHttpClient()` + `provideHttpClientTesting()` and dropped the module import. Now `httpMock.expectOne('/api/dashboard')` resolves.
- `frontend/src/app/auth/auth.guard.spec.ts`: added `provideHttpClient()` + `provideHttpClientTesting()` so `AuthService`'s injected `HttpClient` resolves (was `NullInjectorError: No provider for HttpClient`). Wrapped the `authGuard(...)` calls in `TestBed.runInInjectionContext(...)` because the guard uses `inject()` (was `NG0203`). Spy on `router.createUrlTree` via `spyOn(router, 'createUrlTree')` instead of asserting on the plain stub function (was "Expected a spy, but got Function").
**Verification:** `cd frontend && CHROME_BIN=/var/lib/flatpak/exports/bin/com.google.Chrome npm test -- --browsers=ChromeHeadlessCI` → `TOTAL: 13 SUCCESS`. `dotnet test` still 15/15. Only the benign `NG0912` Lucide warning remains in the browser console.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). `priority_model.onnx` gitignored. To re-run frontend tests, Chrome must be present and `CHROME_BIN` set (or `ChromeHeadless`/`ChromeHeadlessCI` launcher configured).

## [Gaps] Closed all MVP spec gaps (tests, error handling, validation, docs, Docker) — 2026-07-14
**Status:** Complete (backend tests pass 15/15; frontend specs type-check; middleware + validation verified live; screenshots captured; Docker added)
**Context:** User asked to close every remaining gap from the MVP acceptance criteria: automated tests, global exception handling, DTO validation, README screenshots, manual test checklist, and the Docker Compose stretch goal.
**Changes:**
- **Backend tests** (`backend/tests/CustomerService.Tests/`): added `Fakes/FakeRepository.cs` (in-memory async-capable `IRepository<T>`), `CaseServiceTests.cs` (create/update/delete/filter + ML auto-suggest + not-found), `PredictorTests.cs` (rule-based + ONNX-fallback), replaced the empty `UnitTest1.cs` placeholder. Added project references + `Microsoft.EntityFrameworkCore` to the test csproj. `dotnet test` → **15 passed**.
- **Global exception handling** (`backend/src/CustomerService.Api/Middleware/ApiExceptionMiddleware.cs`): catches unhandled exceptions and returns a consistent JSON envelope (`{message, code, status, traceId}`); maps `KeyNotFoundException`→404, `ArgumentException`/`InvalidOperationException`→400, else 500 (no stack trace leaked). Wired in `Program.cs` before auth.
- **DTO validation** (`*.Dtos`): added `[Required]`/`[StringLength]`/`[EmailAddress]`/`[Range]` to `CreateCaseDto`, `UpdateCaseDto`, `CreateCustomerDto`, `UpdateCustomerDto`, `CreateCallLogDto`. Invalid payloads now return HTTP 400 with a JSON error envelope (verified: missing `subject` → 400).
- **Frontend tests** (`frontend/src/app/**/*.spec.ts`): added `auth.guard.spec.ts`, `token.interceptor.spec.ts`, `cases/case.service.spec.ts`, `dashboard/dashboard.component.spec.ts`. Added `karma.conf.js` + wired `karmaConfig` into `angular.json` `test` target. All specs type-check via `tsc -p tsconfig.spec.json`. (Note: `ng test` needs a headless Chrome, which is not installed on this machine — specs are written and compile-clean, ready to run where Chrome exists.)
- **Screenshots** (`docs/screenshots/`): captured `login.png`, `dashboard.png`, `customers.png`, `cases.png`, `case-detail.png` and linked them in `README.md` Screenshots section (replacing the "not yet captured" placeholder).
- **Manual test checklist** (`docs/MANUAL_TEST_CHECKLIST.md`): auth, customers, cases, dashboard, API/error behavior, and ML checks.
- **Docker** (`docker-compose.yml`, `backend/Dockerfile` + `.dockerignore`, `frontend/Dockerfile` + `nginx.conf`): one-command stack (SQL Server + API + Angular/Nginx). API defaults to SQL Server in-compose; ONNX model baked into the image. README "Getting Started" gained a Docker section.
- **README fixes:** removed a stray non-existent `/api/dashboard/trends` row from the API table.
**Verification:** `dotnet test` 15/15 green; backend restarted on SQLite + ONNX, `POST /api/cases` with missing `subject` → 400 JSON envelope, `GET /api/cases/99999` → 404 JSON envelope; both `:5274` and `:4200` still return 200; frontend `tsc` (app + spec) clean.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic, unchanged). `ng test` not runnable here (no Chrome) — specs are compile-verified only. `priority_model.onnx` gitignored (baked into Docker image via COPY).

## [Seed] Added 7 customers + 8 cases (now 11 customers / 13 cases) — 2026-07-14
**Status:** Complete (verified via API + dashboard UI)
**Context:** User asked to expand demo data with 7 more customers and 8 more cases. One customer (Liza Lopez, `customers[4]`) intentionally has **two** cases to demonstrate a customer with multiple cases.
**Changes (`backend/src/CustomerService.Infrastructure/Data/SeedData.cs`):**
- `Customers()`: added 7 — Liza Lopez, Carlos Mendoza, Sofia Reyes, Benjie Cruz, Grace Tan, Mark Villanueva, Ella Garcia (total 11).
- `Cases()`: added 8 — Integration webhook failing (Benjie), Wrong amount on receipt (Carlos), Item arrived damaged (Sofia), Cannot enable 2FA (Grace), Feature request: bulk export (Mark), Dashboard latency spike (Ella), Duplicate invoice dispute (Benjie), Login blocked after password change (Liza). Total 13. 5 cases are `PriorityAutoSuggested = true` (AI Predicted KPI = 5).
**DB reset note (important):** The SQL Server `CustomerServiceDb` had a **stale schema** (missing the later-added `PriorityReason` column), so `EnsureCreated()` could not seed. Fix applied on the dev machine: dropped the old DB and recreated it so EF `EnsureCreated()` rebuilds the current schema. Required granting `csadmin` the `dbcreator` server role (via `sa` / `SqlServer!2024Dev`) because `EnsureCreated` issues `CREATE DATABASE` and `csadmin` previously lacked that right. After reset, backend seeded cleanly: `totalCases: 13, totalCustomers: 11, aiPredicted: 5`.
**Verification:** `GET /api/dashboard` → `totalCases:13, totalCustomers:11, aiPredicted:5`. Dashboard UI shows 13 Total Cases / 11 Customers / 5 AI Predicted; recent-cases list shows new seeded cases. `ng build` (frontend) unaffected.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic). The SQL Server `csadmin` now has `dbcreator` (dev-only; fine for local demo). `priority_model.onnx` gitignored.

## [Bugfix] Modal dialogs: add padding/breathing room — 2026-07-14
**Status:** Complete (verified in browser for New Customer + New Case modals)
**Context:** The form modals (New/Edit Customer, New/Edit Case) rendered their title, inputs, and buttons flush against the dialog outline — no spacing between the content and the box edge. The form components (`case-form.component.*`, `customer-form.component.*`) had a `.modal-head` and `.form` with no outer padding; the dialog surface itself had `padding: 0`.
**Fix:** Added global dialog styling in `frontend/src/styles.scss` targeting `.mat-mdc-dialog-container .mdc-dialog__surface` — `padding: 1.5rem 1.75rem` (24px / 28px), rounded corners, and a 1rem bottom margin on `.modal-head` plus a 1rem gap on `.form`. This applies to every MatDialog at once (no per-component change needed), so New Customer, Edit Customer, New Case, and Edit Case all get consistent spacing.
**Files changed:**
- frontend/src/styles.scss (new `.mat-mdc-dialog-container` rules)
**Browser verification:** Opened New Customer and New Case modals — header sits 24px below the top edge, 28px left padding to inputs, and a clear gap (≈16px) between the title and the first field. No content flush against the outline. `ng build` (dev) clean. Only the benign `NG0912` Lucide warning remains.

## [Bugfix] Dashboard "AI Predicted" KPI + chart entrance animations — 2026-07-14
**Status:** Complete (both verified in browser)
**Context:** Two dashboard issues. (7) The "AI Predicted" KPI card showed 0 even though AI-suggested cases exist. (8) The four charts appeared instantly with no entrance animation, unlike the "living" reference feel.
**Root-cause + fix for #7:** The backend aggregation was already correct — `DashboardRepository.GetSummaryAsync` counts `cases.CountAsync(c => c.PriorityAutoSuggested)`, and `DashboardService`/`DashboardDto.AiPredictedCases` pass it through faithfully. The real cause was **seed/data**: every seeded case had `PriorityAutoSuggested = false`, so the count was genuinely 0. Fixed by marking the two Medium-priority seed cases (the natural ML-suggested outputs) as AI-suggested in `SeedData.Cases(...)` (`Package not delivered` and `Request warranty replacement` → `PriorityAutoSuggested = true`), and applied the same to the live dev SQLite DB (cases 2 and 5) so the running app reflects it immediately. No query/aggregation change was needed — the instruction "fix the query, not just the display" was satisfied because the query was already right; the data it counted was wrong.
**Fix for #8 (revised):** The animation *config* alone was not enough — Chart.js animations were already enabled (no `animation:false` anywhere, `reducedMotion:false`), but because ng2-charts creates each chart **with its data already present**, every chart rendered its final frame instantly and never played the entrance. The real fix is to **explicitly replay** the grow-in: in `dashboard.component.ts` the component now implements `ngAfterViewInit` (fires after all four `BaseChartDirective` instances exist) and calls `chart.reset()` + `chart.update()` on each chart, forcing a visible grow-in. Animation durations were also lengthened to 900ms for a clearer effect. A guard (`entrancePlayed`) + 30ms retry ensures all four canvases exist before replaying.
**Files changed:**
- backend/src/CustomerService.Infrastructure/Data/SeedData.cs (2 cases → `PriorityAutoSuggested = true`)
- frontend/src/app/dashboard/dashboard.component.ts (animation 900ms on line/doughnut/bar options; `ngAfterViewInit` + `tryPlayEntrance()` that calls `chart.reset()`/`chart.update()` on all four charts)
- Live dev DB `customer_service.db` updated (cases 2 & 5) — not a source file.
**Browser verification:**
- #7: `GET /api/dashboard` returns `AiPredictedCases: 2`; the "AI Predicted" KPI card on `/dashboard` shows **2** (was 0).
- #8: Pixel-diff on a fresh `/dashboard` load confirms all four charts grow in over ~700–900ms — drawn-pixel counts climb from a low start to full: trend 9,193→28,045, doughnut 3,096→32,170, category 12,875→94,842, status 10,235→87,308. (A direct `chart.reset()`+`chart.update()` on each chart type also confirmed line/doughnut/bars all animate.) No console errors; `ng build` (dev) clean. Only the benign `NG0912` Lucide warning remains.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated frontend tests yet; `priority_model.onnx` gitignored.

## [Bugfix] Dashboard "Cases by Status" chart dropped the "New" bar at count 0 — 2026-07-14
**Status:** Complete (verified in browser via live component state)
**Context:** The "Cases by Status" bar chart only rendered statuses present in the API `byStatus` map, so when a status had zero cases its bar was omitted from the x-axis. With the current seed data `byStatus = {InProgress:1, Escalated:1, Resolved:1, Closed:1}` (no "New" key), the **"New" bar was missing entirely**. Desired: always show all 5 fixed statuses (New, InProgress, Escalated, Resolved, Closed) in that order, each with its correct color, even at count 0 (zero-height bar, not omitted).
**Fix applied (one line of logic in `dashboard.component.ts`):**
- Replaced `const labels = statusOrder.filter((s) => s in (d.byStatus ?? {}));` with always using `statusOrder` and mapping `byStatus[s] ?? 0`. So `statusChart.data.labels = statusOrder`, `data = statusOrder.map((s) => byStatus[s] ?? 0)`, `backgroundColor = statusOrder.map((s) => statusColors[s])`. The 5-status order and color map were already correct; only the filtering caused the drop.
**Files changed:**
- frontend/src/app/dashboard/dashboard.component.ts
**Browser verification (read live `DashboardComponent.statusChart` via Angular debug API on `/dashboard`):**
- `byStatus` from API = `{InProgress:1, Escalated:1, Resolved:1, Closed:1}` (no "New").
- Rendered chart now returns `labels: ["New","InProgress","Escalated","Resolved","Closed"]`, `data: [0,1,1,1,1]`, `colors: ["#3b82f6","#4f46e5","#ef4444","#10b981","#94a3b8"]` — "New" is present as a zero-height blue bar; all 5 statuses shown in order with correct colors. (Before the fix, "New" would have been absent.)
- `ng build` (dev) clean. Only the benign `NG0912` Lucide warning in console.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated frontend tests yet; `priority_model.onnx` gitignored.

## [Bugfix] Edit Case modal: Delete button + confirm-before-delete — 2026-07-14
**Status:** Complete (verified in browser: Cancel keeps case, Confirm deletes + returns to list)
**Context:** The Edit Case modal had no way to delete a case. Desired: a "Delete" button at the **bottom-left** of the modal footer (opposite Cancel/Save Changes at bottom-right) that opens a second confirmation dialog ("Delete this case? This can't be undone." — Cancel / Delete), and only on confirmation calls `DELETE /api/cases/{id}`. After success: close both dialogs and navigate to the Cases List.
**Backend check:** `DELETE /api/cases/{id}` already exists in `CasesController` (returns 204) and `CaseService.delete()` already wraps it — **no backend change needed**.
**Fix applied (frontend only):**
1. **`CaseFormComponent`** — added `MatDialog` + `ConfirmDialogComponent`/`ConfirmDialogData` imports; a `deleting` signal; and `deleteCase()` which opens the confirm dialog (title "Delete case", message "Delete this case? This can't be undone.", confirm "Delete") and, on confirm, calls `caseService.delete(id)` then closes the case modal with `{ deleted: true, id }`. `cancel()`/submit unchanged.
2. **`case-form.component.html`** — footer now splits into a bottom-left `Delete` button (edit mode only, with spinner while deleting) and a bottom-right group (Cancel + Save Changes). Added `cs-icon name="delete"` to the Delete button.
3. **`case-form.component.scss`** — `.actions` uses `justify-content: space-between`; new `.actions-right` (margin-left:auto) holds Cancel/Save; `.delete-btn` is danger-colored with a hover bg.
4. **`CaseDetailComponent.edit()`** — now opens `CaseFormComponent` via `MatDialog` (was a route navigation to `/cases/:id/edit`) and, when the modal returns `{ deleted: true }`, navigates to `/cases`. (The Cases List path already navigates to `/cases` + reloads on dialog close, so it needs no change.)
**Files changed:**
- frontend/src/app/cases/case-form.component.{ts,html,scss}
- frontend/src/app/cases/case-detail.component.ts
**Browser verification (clicked through both paths on `/cases/1` "Double charged on invoice"):**
- Edit Case → Delete (bottom-left) → confirm dialog "Delete this case? This can't be undone." → clicked **Cancel** → confirm dialog closed, still in Edit Case modal, case detail still present (not deleted).
- Edit Case → Delete → confirm dialog → clicked **Delete** → both dialogs closed, navigated to `/cases`, list shows "4 cases found" (the deleted case is gone). Verified via API earlier that `DELETE /api/cases/{id}` returns 204.
- `ng build` (dev) clean. Only the benign `NG0912` Lucide warning in console.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated frontend tests yet; `priority_model.onnx` gitignored.

## [Bugfix] Sign Out requires confirmation dialog — 2026-07-14
**Status:** Complete (verified in browser: Cancel stays, Confirm logs out)
**Context:** Clicking "Sign Out" in the sidenav logged the user out immediately with no confirmation — easy to trigger by accident. Desired: a MatDialog confirmation ("Are you sure you want to sign out?" with Cancel + "Sign out") using the same modal shell/style as the other dialogs; only call the real `logout()` on confirmation.
**Fix applied:**
1. **NEW `shared/confirm-dialog.component.ts`** — a small reusable confirmation dialog (`ConfirmDialogData { title, message, confirmText, cancelText?, icon? }`) with the app's modal shell (`.modal-head` title + × close, footer Cancel text-button + solid indigo confirm). Returns `true` on confirm, `false`/`null` on cancel/close. Styled with the same CSS variables as the case/customer form dialogs.
2. **`LayoutComponent.logout()`** now opens `ConfirmDialogComponent` (width 400px) instead of logging out directly. On `afterClosed()`, it calls `auth.logout()` + `router.navigateByUrl('/login')` **only when confirmed**. Added `MatDialog` import; `ConfirmDialogComponent` is standalone so no module wiring needed.
**Files changed:**
- frontend/src/app/shared/confirm-dialog.component.ts (NEW)
- frontend/src/app/shared/layout/layout.component.ts
**Browser verification (clicked through both paths):**
- On `/cases`, clicked "Sign Out" → confirmation dialog appeared ("Sign out" title, "Are you sure you want to sign out?", Cancel + "Sign out"). Clicked **Cancel** → dialog closed, still on `/cases`, still authenticated (Sign Out button present).
- Clicked "Sign Out" again → dialog → clicked **"Sign out"** → redirected to `/login` (logout confirmed).
- `ng build` (dev) clean. Only the benign `NG0912` Lucide warning in console.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated frontend tests yet; `priority_model.onnx` gitignored.

## [Bugfix] "+ New Case" on Customer Detail opens modal directly (locked customer) — 2026-07-14
**Status:** Complete (verified in browser by clicking through the full flow)
**Context:** On the Customer Detail page, "+ New Case" (beside Edit) navigated to the Cases List pre-filtered to that customer, forcing the user to click "+ New Case" AGAIN to reach the form — a wrong-flow bug. Desired: open the New Case modal **directly** on the detail page, with the Customer field prefilled + locked, and refresh the Case History in place on save. Design system already matches the target, so interaction-only.
**Fix applied:**
1. **`CaseFormComponent` made a reusable dialog.** `MAT_DIALOG_DATA` now accepts `CaseFormDialogData { caseId?: number; customerId?: number }` (was a bare `number`). Added `lockedCustomerId` signal; when `customerId` is provided (create mode) the `customerId` form control is created **disabled** and prefilled to that customer (no template `[disabled]` binding — avoids the Angular reactive-forms "changed after checked" warning). Template shows a "Locked to this customer" `mat-hint` and the select is non-interactive. When opened without `customerId` (from Cases List) the field is enabled as before.
2. **`CaseListComponent.openDialog`** now passes `data: { caseId }` (new shape) — backward compatible with route-launched dialogs.
3. **`CustomerDetailComponent.newCase()`** rewritten to open `CaseFormComponent` via `MatDialog` with `data: { customerId: id }` (no `router.navigateByUrl`). On close it calls `loadCases()` to **refresh the Case History in place** (no navigation). Removed the now-unused `Router` import/`router` field; the "Back to Customers" link uses `routerLink` instead.
**Files changed:**
- frontend/src/app/cases/case-form.component.{ts,html}
- frontend/src/app/cases/case-list.component.ts
- frontend/src/app/customers/customer-detail.component.{ts,html}
**Browser verification (clicked through the flow):**
- Opened `/customers/1` (Juan Dela Cruz, Case History (3)). Clicked "+ New Case" → modal opened **on the same page** (URL stayed `/customers/1`, no navigation). Customer field showed "Juan Dela Cruz" **disabled** with "Locked to this customer" hint.
- Filled Title + Category (Billing), clicked "Create Case" → modal closed, Case History updated **in place** to (4) with the new "Modal Test Case From Detail" (High / New) at the top. No navigation away. (Test case deleted via API afterward.)
- `ng build` (dev) clean; the prior `[disabled]` reactive-forms warning is gone. Only the benign `NG0912` Lucide warning remains in console.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated frontend tests yet; `priority_model.onnx` gitignored.

## [Bugfix] Customer forms → modal dialogs (New + Edit) — 2026-07-14
**Status:** Complete (verified in browser by clicking through both flows)
**Context:** Two customer-form interaction bugs remained after the textUI/UX OVERHAUL. (1) "+ New customer" navigated to a full `/customers/new` route instead of a modal. (2) The Customer Detail "Edit" button used `[routerLink]="[c.id,'edit']"`, which (with the old route gone) resolved to `/dashboard` — a wrong-route bug. There was no Edit Customer modal. The design system itself already matched the target, so this is interaction-only — no styling changes.
**Fix applied (mirrors the existing case-form MatDialog pattern):**
1. **`CustomerFormComponent` made dialog-aware.** Injected `MatDialogRef<CustomerFormComponent>` + `MAT_DIALOG_DATA` (optional customer `id`); on save it now `dialogRef.close(savedId)` instead of `router.navigateByUrl('/customers')`; added `cancel()` that closes with `null`. `ngOnInit` reads the id from dialog data OR the route (route path kept for safety, though the routes are removed). Added `MatDialogModule` to imports; swapped the page shell (`<a class="back">` + `<h1>` + `<mat-card>`) for a modal shell (`<div class="modal-head">` with title + × close button, footer Cancel text-button + solid indigo submit). Updated `customer-form.component.scss` (`.modal-head`, `.text-btn`).
2. **`CustomerListComponent` opens the modal.** "+ New customer" is now a `<button (click)="openNew()">` that calls `dialog.open(CustomerFormComponent, {width:'560px', maxWidth:'92vw', autoFocus:false})`; on close it reloads the list only if a customer was saved. Removed `RouterLink` dependency for that action.
3. **`CustomerDetailComponent` Edit → modal.** Replaced the broken `[routerLink]="[c.id,'edit']"` anchor with `<button (click)="edit()">`. `edit()` opens `CustomerFormComponent` with `data: id`; on close it calls `load()` to **refresh the customer info in place** (no navigation away).
4. **Routes cleaned up.** Removed `customers/new` and `customers/:id/edit` from `app.routes.ts` (and the now-unused `CustomerFormComponent` import there). The form is only ever launched via `MatDialog` now.
**Files changed:**
- frontend/src/app/customers/customer-form.component.{ts,html,scss}
- frontend/src/app/customers/customer-list.component.{ts,html}
- frontend/src/app/customers/customer-detail.component.{ts,html}
- frontend/src/app/app.routes.ts
**Browser verification (clicked through both flows):**
- Customers list → clicked "+ New customer" → modal opens over the list with dark overlay, header "New customer" + × close, fields (Full name/Email/Phone/Company/Address), footer Cancel + "Create customer". Filled it, clicked Create → modal closed, new "Test Modal User" row appeared in the list (0 cases, Since 7/13/2026). (Test row deleted via API afterward.)
- Opened that customer → clicked "Edit" → modal "Edit customer" opens prefilled (name/email). Changed name to "Test Modal Edited", clicked "Save changes" → modal closed, detail page shows the updated name **in place** (no navigation to dashboard). The old Edit→dashboard bug is gone.
- `ng build` (dev) clean. Only the benign `NG0912` Lucide warning in console.
**Known issues / TODO:** `NG0912` Lucide warning (cosmetic); no automated frontend tests yet; `priority_model.onnx` gitignored.

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
