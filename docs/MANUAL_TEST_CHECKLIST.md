# Manual Test Checklist

Step-by-step checks to verify the app end-to-end. Run the backend (`:5274`) and
frontend (`:4200`) first — see the [README](../README.md#getting-started).

**Demo credentials:** `admin` / `Passw0rd!` (also `agent` / `maria`).

---

## 1. Authentication
- [ ] Visiting any route while logged out redirects to `/login`.
- [ ] Logging in with `admin` / `Passw0rd!` succeeds and lands on `/dashboard`.
- [ ] Wrong password shows an error and stays on `/login`.
- [ ] Clicking **Sign Out** clears the session and returns to `/login`.

## 2. Customers
- [ ] `/customers` lists seeded customers with case counts.
- [ ] Search box filters by name / email / phone (debounced).
- [ ] **New Customer** opens a modal; submitting with valid data adds a row.
- [ ] Submitting with an empty Name or invalid Email shows a validation error.
- [ ] Clicking a customer opens its detail page (with case history).
- [ ] **Edit** updates the customer; **Delete** (admin) removes it.

## 3. Cases
- [ ] `/cases` lists cases with status / priority / category pills.
- [ ] Filters (status, priority, category, free-text search) narrow the list.
- [ ] **New Case** opens a modal; the **Get AI suggestion** action previews a
      priority (Low / Medium / High) from the description keywords.
- [ ] Creating a case with no explicit priority stores the ML-suggested value
      and flags it as AI-predicted on the dashboard.
- [ ] Opening a case shows the **AI Priority Prediction** panel (suggested vs.
      final) and the **Call & Follow-up Log**.
- [ ] Adding a call log appends it to the list.
- [ ] **Edit Case → Set Priority / Update Status** overrides the values; the
      "AI Predicted" flag clears on manual override.
- [ ] **Delete** (with confirm dialog) removes the case and returns to the list.

## 4. Dashboard
- [ ] KPI cards show: Total Cases, Open Cases, High Priority, Resolved,
      Customers, AI Predicted.
- [ ] Trend line chart renders case creation over time.
- [ ] Priority donut, category bar, and status bar charts render (status chart
      always shows all five statuses, even at count 0).
- [ ] Recent Cases list links to case detail pages.

## 5. API / Error Behavior (backend)
- [ ] `POST /api/cases` with a missing `subject` returns HTTP 400 with a JSON
      error envelope (no stack trace).
- [ ] `GET /api/cases/{missingId}` returns HTTP 404 with a JSON error envelope.
- [ ] Swagger UI is available at `http://localhost:5274/swagger`.

## 6. ML Model
- [ ] `POST /api/ml/predict-priority` returns a priority + plain-English reason.
- [ ] A description containing "urgent"/"refund"/"broken" trends toward higher
      priority than a neutral description.
