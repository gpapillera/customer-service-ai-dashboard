# Customer Service AI Dashboard — MVP Build Prompt

**Repo name:** `customer-service-ai-dashboard`
**Purpose of this document:** a single, self-contained specification you can hand to a developer — or paste into an AI coding assistant such as Claude Code — to build this app end-to-end as a portfolio project. It doubles as the project's technical design document.

---

## 0. How to Use This Prompt

1. Read Sections 1–14 once to understand the full scope.
2. Build in the order given in **Section 15 (Build Phases)**. Do not skip ahead — each phase should produce runnable, documented code before the next begins.
3. Follow the **Code Documentation Standards** in Section 13 for every file you write.
4. When you reach the end, use **Section 18** as a ready-to-paste prompt for an AI coding agent, and use the separate `README.md` as the project's public-facing documentation.

---

## 1. Project Overview

**Name:** Customer Service AI Dashboard
**One-line pitch:** A web app that lets a support team manage customers and cases, log calls/follow-ups, and see an AI-suggested priority (Low / Medium / High) for every new case, alongside a dashboard of case volume and trends.

**Why this project:** it mirrors real customer-service work — customer records, call tracking, follow-ups, Excel/CRM-style reporting — while adding a simple, explainable AI layer and a dashboard. It is meant to demonstrate: full-stack web development, database design, data cleaning, a basic ML pipeline, and data visualization, in one coherent app.

**MVP definition of "done":** a user can log in, create/search customers, open a case, log a call/follow-up, see an AI-suggested priority when the case is created, and view a dashboard with case totals and weekly/monthly trend charts.

---

## 2. Tech Stack & Rationale

| Layer | Technology | Why |
|---|---|---|
| Frontend | Angular + TypeScript + HTML/CSS | Component-based SPA, strong typing, widely used in enterprise apps |
| UI/Charts | Angular Material + Chart.js (via `ng2-charts`) | Fast to build clean UI and dashboard charts |
| Backend | ASP.NET Core Web API (C#) | Matches enterprise/Microsoft-stack roles (e.g. D365-adjacent environments) |
| ORM | Entity Framework Core | Code-first migrations, clean data access layer |
| Database | MS SQL Server | Standard enterprise RDBMS |
| Data cleaning | Python (pandas) | Fast, readable scripting for cleaning raw exports |
| ML model | scikit-learn (training) → ONNX → ML.NET/OnnxRuntime (inference in C#) | Shows both Python data-science skill and C# integration skill; keeps the running app to one deployable backend |
| Auth | JWT (JSON Web Tokens) | Stateless, standard for SPA + API architecture |

> If time is short, the ML step can be built entirely in ML.NET (no Python/ONNX bridge) — see Section 8's "fast path."

---

## 3. High-Level Architecture

```
Angular SPA  ──HTTPS/JWT──▶  ASP.NET Core Web API  ──EF Core──▶  SQL Server
                                     ▲
                                     │ loads trained model at startup
                       priority_model.onnx (or ML.NET .zip)
                                     ▲
                     Python: clean_data.py → train_model.py   (offline, run once/periodically)
```

Text version of the diagram above (also included as a Mermaid diagram in `README.md`):
- The Angular frontend only ever talks to the ASP.NET Core Web API over HTTPS, authenticated with a JWT.
- The API is the only thing that talks to SQL Server (via EF Core) and to the ML model.
- The ML model is trained **offline** by a Python script and loaded into the API at runtime — the API does not train models live.

---

## 4. Repository Structure

```
customer-service-ai-dashboard/
├── backend/                     # ASP.NET Core Web API
│   ├── src/
│   │   ├── Controllers/
│   │   ├── Services/
│   │   ├── Repositories/
│   │   ├── Models/               # EF Core entities
│   │   ├── DTOs/
│   │   ├── Data/                 # DbContext, migrations
│   │   └── ML/                   # model loader + prediction service
│   └── tests/
├── frontend/                     # Angular app
│   └── src/app/
│       ├── auth/
│       ├── customers/
│       ├── cases/
│       ├── dashboard/
│       └── shared/
├── ml/                            # Python data cleaning + model training
│   ├── clean_data.py
│   ├── train_model.py
│   ├── requirements.txt
│   └── data/ (raw/, cleaned/)
├── database/
│   └── schema.sql
├── docs/
│   ├── CODE_DOCUMENTATION.md
│   ├── MODEL_CARD.md
│   └── screenshots/
├── README.md
└── .gitignore
```

---

## 5. Database Design

### Tables

- **Users** — login accounts for agents/admins
- **Customers** — the people/companies contacting support
- **Categories** — complaint/concern categories (e.g. Billing, Technical, Shipping/Supply Chain, Product Quality, General Inquiry)
- **Cases** — one row per support case/ticket
- **CallLogs** — call/follow-up records tied to a case

### Schema (SQL Server DDL)

```sql
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(150) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    Role NVARCHAR(20) NOT NULL DEFAULT 'Agent',   -- Admin | Agent
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Customers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(150) NULL,
    Phone NVARCHAR(30) NULL,
    Company NVARCHAR(150) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL
);

CREATE TABLE Categories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(255) NULL
);

CREATE TABLE Cases (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT NOT NULL FOREIGN KEY REFERENCES Customers(Id),
    CategoryId INT NOT NULL FOREIGN KEY REFERENCES Categories(Id),
    AssignedAgentId INT NULL FOREIGN KEY REFERENCES Users(Id),
    Subject NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Open',        -- Open | InProgress | Resolved | Closed
    Priority NVARCHAR(10) NOT NULL DEFAULT 'Medium',    -- Low | Medium | High (agent-confirmed)
    PredictedPriority NVARCHAR(10) NULL,                -- Low | Medium | High (model output)
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    DueDate DATETIME2 NULL,
    ResolvedAt DATETIME2 NULL
);

CREATE TABLE CallLogs (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CaseId INT NOT NULL FOREIGN KEY REFERENCES Cases(Id),
    AgentId INT NOT NULL FOREIGN KEY REFERENCES Users(Id),
    ContactType NVARCHAR(20) NOT NULL,   -- Call | Email | InPerson | Chat
    Notes NVARCHAR(MAX) NULL,
    Outcome NVARCHAR(100) NULL,
    FollowUpDate DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
```

Seed `Categories` with at least: `Billing`, `Technical Support`, `Shipping / Supply Chain`, `Product Quality`, `General Inquiry`.

---

## 6. Backend (ASP.NET Core Web API)

**Architecture:** `Controllers → Services → Repositories → EF Core DbContext`. Controllers stay thin (no business logic); services hold logic; repositories hold data access.

### Endpoints (MVP)

| Method | Endpoint | Description | Auth |
|---|---|---|---|
| POST | `/api/auth/login` | Authenticate, return JWT | No |
| GET | `/api/customers` | List/search customers (`search`, `page`, `pageSize`) | Yes |
| GET | `/api/customers/{id}` | Customer detail + case history | Yes |
| POST | `/api/customers` | Create customer | Yes |
| PUT | `/api/customers/{id}` | Update customer | Yes |
| DELETE | `/api/customers/{id}` | Delete customer | Yes (Admin) |
| GET | `/api/cases` | List/filter cases (`status`, `priority`, `categoryId`, `dateFrom`, `dateTo`, `search`, `page`) | Yes |
| GET | `/api/cases/{id}` | Case detail incl. call logs | Yes |
| POST | `/api/cases` | Create case (auto-triggers ML priority prediction) | Yes |
| PUT | `/api/cases/{id}` | Update case | Yes |
| PATCH | `/api/cases/{id}/status` | Update case status | Yes |
| GET | `/api/cases/{id}/calllogs` | List call/follow-up logs | Yes |
| POST | `/api/cases/{id}/calllogs` | Add a call/follow-up log | Yes |
| GET | `/api/dashboard/summary` | Total / pending / resolved counts | Yes |
| GET | `/api/dashboard/trends?period=weekly\|monthly` | Chart data | Yes |
| GET | `/api/dashboard/categories-breakdown` | Case counts by category | Yes |
| POST | `/api/ml/predict-priority` | Predict priority from case features | Yes |

### Requirements
- JWT authentication + role-based authorization (`Admin`, `Agent`).
- Passwords hashed with BCrypt — never store plaintext.
- FluentValidation (or DataAnnotations) on all incoming DTOs.
- Global exception-handling middleware returning consistent error JSON.
- AutoMapper (or manual mapping) between entities and DTOs — never expose EF entities directly.
- Swagger/OpenAPI enabled for interactive API docs.
- CORS configured to allow only the Angular dev/prod origin.

---

## 7. Frontend (Angular)

### Modules & Components

- **AuthModule:** `LoginComponent`, `AuthGuard`, `AuthService`, `TokenInterceptor`
- **CustomersModule:** `CustomerListComponent`, `CustomerDetailComponent`, `CustomerFormComponent`, `CustomerService`
- **CasesModule:** `CaseListComponent` (with filters), `CaseDetailComponent`, `CaseFormComponent`, `CallLogFormComponent`, `CaseService`
- **DashboardModule:** `DashboardComponent` (KPI cards + charts), `ChartService`
- **SharedModule:** `NavbarComponent`, `SidebarComponent`, `LoadingSpinnerComponent`, `ConfirmDialogComponent`, shared pipes

### Requirements
- Route guards protect everything except `/login`.
- An `HttpInterceptor` attaches the JWT to every request and redirects to `/login` on 401.
- State kept simple for MVP: services + RxJS `BehaviorSubject`s (no NgRx needed at this scale).
- Reactive Forms with inline validation messages for all create/edit forms.
- Responsive layout (desktop-first is fine for MVP; don't need full mobile support).

---

## 8. AI/ML Priority Prediction

**Goal:** predict `Priority` (Low / Medium / High) for a new case from a small set of features, so agents get a starting suggestion they can override.

### Features
- `CategoryId` (encoded)
- `CustomerPriorCaseCount` — number of prior cases from this customer
- `DaysSinceLastContact`
- `ChannelType` (Call / Email / Chat / InPerson)
- Keyword flags in the case description (e.g. contains "urgent", "broken", "refund", "delay", "no response")

### Training data
Because there's no real historical dataset, generate a **synthetic, rule-labeled dataset** for the MVP:
- Example rule: `Priority = High` if category is `Shipping / Supply Chain` or `Product Quality` **and** description contains an urgency keyword; `Priority = Medium` if only one condition holds; else `Low`.
- Generate a few hundred to a few thousand synthetic rows with this rule plus randomized noise, so the model has to actually learn a pattern rather than memorize the rule.
- Document this clearly in `docs/MODEL_CARD.md` as a limitation, with instructions for retraining on real historical case data later.

### Model
- Multiclass classifier: start with a **Decision Tree** or **Random Forest** (scikit-learn) — simple, explainable, good enough for 3 classes.
- Evaluate with accuracy + confusion matrix; log both to `docs/MODEL_CARD.md`.
- Export via `skl2onnx` to `priority_model.onnx`.
- Load the ONNX model in the ASP.NET Core backend using `Microsoft.ML.OnnxRuntime` inside a small `PriorityPredictionService`; call it from `POST /api/cases` right after a case is created, storing the result in `PredictedPriority`.

### Fast path (if time-constrained)
Skip Python/ONNX entirely and train a multiclass classifier directly in **ML.NET** (`MLContext`, `SdcaMaximumEntropy` trainer) using the same synthetic dataset generated in C#. One stack, no bridge — still satisfies "simple AI/ML model that predicts case priority."

---

## 9. Data Cleaning Script (`ml/clean_data.py`)

Simulates cleaning a raw CSV export (like one pulled from D365 or Excel) before it's loaded into SQL Server.

**Input columns (raw):** Customer Name, Email, Phone, Case Subject, Description, Category, Date Created, Status

**Cleaning steps:**
1. Drop exact duplicate rows.
2. Normalize phone numbers (strip non-digits, consistent format).
3. Normalize emails (lowercase, trim whitespace).
4. Parse inconsistent date formats into ISO 8601.
5. Fill missing `Category` with `"Uncategorized"`.
6. Trim whitespace from all text fields.
7. Flag (don't silently drop) rows with missing required fields (Customer Name, Case Subject) into a `rejected_rows.csv` for manual review.
8. Write the cleaned result to `ml/data/cleaned/cases_cleaned.csv` and log a short summary (rows in, rows out, rows rejected) to the console.

---

## 10. Dashboard & Charts

**KPI cards:** Total Cases, Pending (Open + InProgress), Resolved, Average Resolution Time.

**Charts:**
- Line chart — case volume by week/month (toggle).
- Bar or pie chart — case count by category.
- Bar chart — case count by priority (Low/Medium/High), split by predicted vs. agent-confirmed.

**Filters (apply to dashboard and case list):** date range, category, status, priority.

---

## 11. Search & Filter

- **Customers:** search by name, email, or phone (single search box, debounced).
- **Cases:** filter by status, category, priority, date range, plus free-text search across subject/description.

---

## 12. Authentication & Authorization

- Login page issues a JWT on success; token stored in memory (or `sessionStorage` if simplicity is preferred, with a note that it's not the most secure option for production).
- Two roles: `Admin` (full access, including delete) and `Agent` (day-to-day case work, no delete).
- Passwords hashed with BCrypt on the backend; never logged or returned in any API response.

---

## 13. Code Documentation Standards

Every file in this project should be documented well enough that a new developer can understand it without reading every line. Use these conventions consistently:

**C# — XML doc comments on every public class/method:**
```csharp
/// <summary>
/// Retrieves a paginated list of customer service cases, optionally filtered
/// by status, priority, category, and date range.
/// </summary>
/// <param name="status">Optional case status filter (Open, InProgress, Resolved, Closed).</param>
/// <param name="priority">Optional priority filter (Low, Medium, High).</param>
/// <param name="page">Page number, starting at 1.</param>
/// <param name="pageSize">Number of records per page.</param>
/// <returns>A paginated list of <see cref="CaseDto"/> objects.</returns>
[HttpGet]
public async Task<ActionResult<PagedResult<CaseDto>>> GetCases(
    [FromQuery] string? status,
    [FromQuery] string? priority,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
{
    // implementation
}
```

**TypeScript/Angular — JSDoc on every public service method:**
```typescript
/**
 * Fetches a filtered, paginated list of cases from the API.
 *
 * @param filters - Optional filter criteria (status, priority, category, dateRange)
 * @param page - Page number to retrieve
 * @returns Observable emitting the paginated case results
 */
getCases(filters: CaseFilter, page: number = 1): Observable<PagedResult<Case>> {
  // implementation
}
```

**Python — Google-style docstrings on every function:**
```python
def clean_customer_records(df: pd.DataFrame) -> pd.DataFrame:
    """Cleans raw customer/case export data before loading into SQL Server.

    Removes duplicate rows, normalizes phone numbers and emails, parses
    inconsistent date formats, and fills missing category values with
    'Uncategorized'.

    Args:
        df: Raw DataFrame loaded from the exported CSV file.

    Returns:
        A cleaned DataFrame ready for database insertion.
    """
```

**Additional rules:**
- Inline `//` / `#` comments only where logic isn't self-explanatory (don't narrate obvious code).
- Consistent naming: `PascalCase` for C#, `camelCase` for TypeScript, `snake_case` for Python.
- Maintain `docs/CODE_DOCUMENTATION.md` — a short, module-by-module explanation of what each folder/file is responsible for (updated as the project grows, not written all at once at the end).
- Maintain `docs/MODEL_CARD.md` — what the ML model predicts, what features it uses, how it was trained, its accuracy, and its known limitations (synthetic training data).

---

## 14. Testing (MVP-light)

- **Backend:** xUnit tests for service-layer logic and the priority-prediction service (mock the ONNX/ML.NET call).
- **Frontend:** a handful of Jasmine/Karma tests for `AuthGuard`, `CaseService`, and the dashboard component's data-binding.
- **Manual test checklist** (put this in `docs/`): login, create customer, create case (verify predicted priority appears), log a call, filter case list, view dashboard charts, log out.

---

## 15. Build Phases (Step-by-Step Execution Plan)

1. **Scaffold** the repo structure (Section 4).
2. **Database** — write `schema.sql`, create EF Core models + migrations, seed `Categories` and a demo `Admin` user.
3. **Backend core** — Auth, Customers, Cases, CallLogs controllers/services/repositories, Swagger enabled.
4. **Data cleaning** — write and test `clean_data.py` against a small sample raw CSV.
5. **ML model** — generate synthetic labeled data, train, evaluate, export (ONNX or ML.NET), write `MODEL_CARD.md`.
6. **ML integration** — load model in backend, wire into `POST /api/cases`.
7. **Frontend scaffolding** — Angular app shell, routing, Auth module, HTTP interceptor.
8. **Customer & Case UI** — list, detail, create/edit forms, call-log form.
9. **Dashboard** — KPI cards + charts, wired to `/api/dashboard/*`.
10. **Search & filter** — across customers and cases.
11. **Testing** — backend and frontend tests from Section 14.
12. **Documentation** — finalize `README.md`, `CODE_DOCUMENTATION.md`, `MODEL_CARD.md`, and take screenshots for the README.
13. *(Optional stretch)* Docker Compose for one-command local run.

---

## 16. Definition of Done (MVP Acceptance Criteria)

- [ ] User can log in and is redirected to `/login` if unauthenticated.
- [ ] Customers can be created, viewed, searched, edited, deleted.
- [ ] Cases can be created against a customer, with a category and description.
- [ ] On case creation, a predicted priority (Low/Medium/High) is shown and can be overridden by the agent.
- [ ] Call/follow-up logs can be added to a case and are listed on the case detail page.
- [ ] Dashboard shows total/pending/resolved counts and at least one weekly/monthly trend chart.
- [ ] Case list supports filtering by status, priority, category, and date range.
- [ ] `clean_data.py` runs against a sample raw CSV and produces a cleaned CSV.
- [ ] README includes setup instructions, architecture diagram, and screenshots.
- [ ] All public methods/classes are documented per Section 13.

---

## 17. Stretch Goals (Post-MVP)

- Sentiment analysis on complaint text (NLP) instead of keyword flags.
- Email/SMS notifications for overdue follow-ups.
- Role-based dashboard views (Admin vs. Agent).
- Docker Compose setup for one-command local deployment.
- CI/CD pipeline (GitHub Actions) running backend/frontend tests on push.
- Retrain the ML model on real historical case data once available.

---

## 18. Ready-to-Paste Prompt for an AI Coding Assistant

Copy the block below into Claude Code (or a similar coding agent) to execute the build phase-by-phase.

```text
You are an expert full-stack engineer building a portfolio-quality MVP web app
called "Customer Service AI Dashboard" (repo: customer-service-ai-dashboard).

GOAL: Build a working, cleanly documented MVP demonstrating customer service
case management combined with a simple AI priority-prediction model, for a
job application highlighting AI-in-customer-service and supply-chain skills.

TECH STACK:
- Backend: ASP.NET Core 8 Web API (C#), Entity Framework Core, SQL Server
- Frontend: Angular (latest LTS), TypeScript, Angular Material, ng2-charts
- ML: Python (pandas, scikit-learn) for data cleaning + model training;
  export via ONNX and run inference in the backend with
  Microsoft.ML.OnnxRuntime (or train directly in ML.NET if you want a
  single-stack fast path)
- Auth: JWT with roles (Admin, Agent)

BUILD IN THIS ORDER, producing runnable code at each phase before moving on:
1. Scaffold repo structure: /backend, /frontend, /ml, /database, /docs
2. Create the SQL Server schema (Users, Customers, Categories, Cases,
   CallLogs — see schema below), EF Core migrations, and seed data.
3. Build the backend: layered architecture (Controllers -> Services ->
   Repositories -> EF Core), JWT auth, CRUD endpoints for Customers, Cases,
   CallLogs, plus a Dashboard summary endpoint. Enable Swagger. Add XML doc
   comments on every public class and method.
4. Write ml/clean_data.py: ingest a raw CSV (simulating a D365/Excel
   export), dedupe, normalize phone/email, parse dates, fill missing
   categories, output a cleaned CSV. Use Google-style docstrings.
5. Generate a synthetic, rule-labeled dataset and train a multiclass
   classifier (Decision Tree or Random Forest) predicting case Priority
   (Low/Medium/High) from category, customer's prior case count, days
   since last contact, and complaint-keyword flags. Export the model and
   document its accuracy/limitations in docs/MODEL_CARD.md.
6. Wire the trained model into the backend so new cases get an
   auto-suggested priority (used inside POST /api/cases), while agents can
   still override it manually.
7. Build the Angular frontend: Auth module (login, guard, interceptor),
   Customers module (list, detail, form, search), Cases module (list with
   filters, detail, create/edit, status updates, call/follow-up log),
   Dashboard module (KPI cards + weekly/monthly trend chart + category
   breakdown chart). Add JSDoc comments on all public service methods.
8. Add search & filtering across customers (name/email/phone) and cases
   (status, priority, category, date range).
9. Add minimal automated tests: xUnit for backend services and the ML
   prediction path; a few Jasmine/Karma tests for critical components.
10. Write README.md (features, tech stack, architecture diagram, setup
    instructions, screenshots), docs/CODE_DOCUMENTATION.md (module-by-module
    explanation), and docs/MODEL_CARD.md.

NON-NEGOTIABLE CODE QUALITY REQUIREMENTS:
- Every public C# class/method: XML doc comments (<summary>, <param>, <returns>)
- Every public Angular service method / complex component: JSDoc comments
- Every Python function: Google-style docstrings
- Consistent naming (PascalCase C#, camelCase TS, snake_case Python)
- No secrets committed; use appsettings.Development.json / .env, both
  excluded via .gitignore
- Meaningful commit message per phase

DATABASE SCHEMA:
[paste the SQL DDL from Section 5 of MVP_BUILD_PROMPT.md here]

Deliver working, runnable code at the end of each phase before starting the
next. Ask before making any assumption that would materially change scope.
```

---

## Why This Project Fits the Role

This app is designed to translate hands-on customer-service experience — customer records, call tracking, follow-ups, D365 usage, Excel-based reporting — into a demonstrable software project that also shows the AI-in-customer-service and data-visualization angle a supply-chain/customer-service-focused AI role would care about. The database, dashboard, and search/filter features map directly to CRM-style daily work; the ML priority model and data-cleaning script map directly to the "AI integration" part of the job description.
