# Phase 24w — Customer Card Last Activity Display

## Goal
Replace the static "Since {createdAt}" date at the bottom-right of each customer card with a **live last-activity indicator** showing the most recent activity across all of that customer's cases, and when it happened.

---

## Design

**Before (current):**
```
┌──────────────────────────────────┐
│ ...                              │
│  ● 1 active   1 total cases      │
│                    Since 4/20/26 │
└──────────────────────────────────┘
```

**After:**
```
┌──────────────────────────────────┐
│ ...                              │
│  ● 1 active   1 total cases      │
│              Messaged customer   │
│               Jul 24, 2:30 PM    │
└──────────────────────────────────┘
```

Or fallback when no activity exists:
```
┌──────────────────────────────────┐
│ ...                              │
│  ● 0 active    0 total cases     │
│                  Since 4/20/26   │
└──────────────────────────────────┘
```

---

## Activity Sources (priority by recency)

| Source | Timestamp | Description |
|---|---|---|
| Case created | `Case.CreatedAtUtc` | "Opened case #{id}" |
| Case updated (status change) | `Case.UpdatedAtUtc` | "Resolved case #{id}" / "Closed case #{id}" / "Updated case #{id}" |
| Call log added | `CallLog.CreatedAtUtc` | "Updated call log" |
| Staff comment | `CaseComment.CreatedAtUtc` (AuthorUserId != null) | "Messaged customer" |
| Customer comment | `CaseComment.CreatedAtUtc` (AuthorCustomerId != null) | "Customer replied" |
| Email sent | `Notification.CreatedAtUtc` (Channel.Email) | "Sent email" |

The **latest timestamp** across all sources wins. Ties broken by whichever source came first in this priority order.

---

## Implementation Phases

### Phase 1 — Backend DTO + Navigation

**Files:**
- `backend/src/CustomerService.Application/Dtos/CustomerDtos.cs`
- `backend/src/CustomerService.Domain/Entities/Case.cs`
- `backend/src/CustomerService.Infrastructure/Data/AppDbContext.cs`

**Changes:**
1. Add `LastActivityAtUtc` (DateTime?) and `LastActivityDescription` (string?) to `CustomerDto`.
2. Add `ICollection<Notification> Notifications` navigation to `Case` entity.
3. Wire `Case.Notifications` relationship in `AppDbContext` (currently `HasOne<Case>().WithMany()` has no inverse).

### Phase 2 — Backend Service Logic

**File:** `backend/src/CustomerService.Application/Services/CustomerService.cs`

**Changes:**
1. Inject `IRepository<Notification>` via constructor.
2. Add `ComputeLastActivity(Customer c)` private static helper that scans all cases (with their call logs, comments, notifications) and returns `(DateTime? atUtc, string? description)` for the most recent activity.
3. Update `GetAllAsync` and `SearchAsync` — change from pure `Select()` projection to `Include()` + `ToListAsync()` + in-memory mapping so navigation data is available for `ComputeLastActivity`.
4. Update `GetByIdAsync` — extend `.Include()` chain to cover call logs, comments, notifications.
5. Update `ToDto()` to map the new fields.

### Phase 3 — Frontend Model + Display

**Files:**
- `frontend/src/app/shared/models.ts`
- `frontend/src/app/customers/customer-list.component.ts`
- `frontend/src/app/customers/customer-list.component.html`
- `frontend/src/app/customers/customer-list.component.scss`

**Changes:**
1. Add `lastActivityAtUtc` and `lastActivityDescription` to `Customer` interface.
2. Add `formatDateTime()` method to component (shows "MMM DD, HH:MM AM/PM").
3. Replace `Since {{ formatDate(c.createdAtUtc) }}` with conditional last-activity display.
4. Add `.last-activity`, `.activity-desc`, `.activity-time` SCSS styles.

---

## Verification

1. `dotnet build CustomerServiceApi.sln` → 0 errors
2. `ng build` → 0 errors
3. Backend startup re-creates schema with `Notifications` navigation → clean seed
4. `GET /api/customers` returns `lastActivityAtUtc`/`lastActivityDescription` per customer
5. Browser check — Customers page:
   - Customer with comments → "Messaged customer" / "Customer replied" + timestamp
   - Customer with resolved/closed case → "Resolved case #X" / "Closed case #X"
   - Customer with call logs → "Updated call log" + timestamp
   - New customer with no cases → fallback "Since {date}"
6. Active case pill behavior unchanged
