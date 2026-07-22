# Manual Test Checklist

Step-by-step checks to verify the app end-to-end. Run the backend (`:5274`) and
frontend (`:4200`) first — see the [README](../README.md#getting-started).

**Demo credentials:** `admin` / `Passw0rd!` (also `agent` / `maria`).

---

## 1. Authentication
- [x] Visiting any route while logged out redirects to `/login`.
- [x] Logging in with `admin` / `Passw0rd!` succeeds and lands on `/dashboard`.
- [x] Wrong password shows an error and stays on `/login`.
- [x] Clicking **Sign Out** clears the session and returns to `/login`.

## 2. Customers
- [x] `/customers` lists seeded customers with case counts.
- [x] Search box filters by name / email / phone (debounced).
- [x] **New Customer** opens a modal; submitting with valid data adds a row.
- [x] Submitting with an empty Name or invalid Email shows a validation error.
- [x] Clicking a customer opens its detail page (with case history).
- [x] **Edit** updates the customer; **Delete** (admin) removes it.

## 3. Cases
- [x] `/cases` lists cases with status / priority / category pills.
- [x] Filters (status, priority, category, free-text search) narrow the list.
- [x] **New Case** opens a modal; the **Get AI suggestion** action previews a
      priority (Low / Medium / High) from the description keywords.
- [x] Creating a case with no explicit priority stores the ML-suggested value
      and flags it as AI-predicted on the dashboard.
- [x] Opening a case shows the **AI Priority Prediction** panel (suggested vs.
      final) and the **Call & Follow-up Log**.
- [x] Adding a call log appends it to the list.
- [x] **Edit Case → Set Priority / Update Status** overrides the values; the
      "AI Predicted" flag clears on manual override.
- [x] **Delete** (with confirm dialog) removes the case and returns to the list.

## 4. Dashboard
- [x] KPI cards show: Total Cases, Open Cases, High Priority, Resolved,
      Customers, AI Predicted.
- [x] Trend line chart renders case creation over time.
- [x] Priority donut, category bar, and status bar charts render (status chart
      always shows all five statuses, even at count 0).
- [x] Recent Cases list links to case detail pages.

## 5. API / Error Behavior (backend)
- [x] `POST /api/cases` with a missing `subject` returns HTTP 400 with a JSON
      error envelope (no stack trace).
- [x] `GET /api/cases/{missingId}` returns HTTP 404 with a JSON error envelope.
- [x] Swagger UI is available at `http://localhost:5274/swagger`.

## 6. ML Model
- [x] `POST /api/ml/predict-priority` returns a priority + plain-English reason.
- [x] A description containing "urgent"/"refund"/"broken" trends toward higher
      priority than a neutral description.
