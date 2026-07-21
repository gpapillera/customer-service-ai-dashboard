# Phase 9 Verification Report — Agent Conversations Tab + New-Message Notification

**Date:** 2026-07-21  
**Verified by:** Automated API tests (curl) + browser UI check + SQL cross-check  
**Status:** ✅ RESOLVED — All 6 gaps from initial verification have been fixed (see [PROGRESS_LOG.md](./PROGRESS_LOG.md) "Phase 9 — Gap Fixes" entry)

### Gap Fix Summary (2026-07-21)
| # | Gap | Severity | Status |
|---|-----|----------|--------|
| 1 | `ConversationReadStates` table missing | 🔴 CRITICAL | ✅ Fixed — `EnsureConversationReadStatesTable()` added to `Program.cs` |
| 2 | Notifications not filtered by recipient | 🟡 MEDIUM | ✅ Fixed — `GetAllAsync`/`GetSummaryAsync` accept `recipientUserId` |
| 3 | No comment thread on case detail page | 🟡 MEDIUM | ✅ Fixed — full comment section added to `case-detail` component |
| 4 | No mark-as-read endpoint | 🟡 MEDIUM | ✅ Fixed — `POST /api/cases/{id}/conversations/mark-read` + auto-mark on case open |
| 5 | Admin PUT assignment issue | 🟢 LOW | ✅ Verified correct — code path works; test was sequencing artifact |
| 6 | Schema drift documentation | 🟢 LOW | ✅ Documented in `PROGRESS_LOG.md` + `AGENTS.md` |

---

## Environment
- Backend: `http://localhost:5274` (ASP.NET Core 8, SQL Server)
- Frontend: `http://localhost:4200` (Angular 18)
- Test users: `admin`/`agent`(agent-001)/`maria`(agent-002) — all `Passw0rd!`
- Test customer: `phase9test@example.com` / `TestPass123!` (customer id 19)
- Test case: Case#19 "Phase 9 test case - billing dispute" (assigned to agent-002, Maria)

---

## Scenario Results

### Scenario 1: Customer posts comment → Agent sees notification + conversation
**✅ PASS (backend) — ⚠️ PARTIAL (frontend)**

| Step | Result |
|------|--------|
| Customer posts comment on assigned case | ✅ `POST /api/customer-portal/cases/19/comments` → 201 |
| `NewCustomerMessage` notification created | ✅ Notification #40, `Type:NewCustomerMessage`, `Recipient:agent-002` |
| Conversation appears in assigned agent's `my-conversations` | ✅ Case#19 appears in Maria's (agent-002) conversations with `unread:true` |
| Conversation snippet shows correct text | ✅ Snippet truncated to 140 chars |
| Click conversation → navigates to correct case | ✅ Navigates to `/cases/19` |

### Scenario 2: Other agent doesn't see leaked data
**✅ PASS (conversations) — ❌ FAIL (notifications)**

| Step | Result |
|------|--------|
| Agent-001 (Grace) does NOT see Case#19 in conversations | ✅ No data leakage in conversation list |
| Agent-001 does NOT see NewCustomerMessage notification for Case#19 | ❌ **NOTIFICATION LEAKAGE** — notification #40 visible to ALL agents |

### Scenario 3: Comment on unassigned case
**✅ PASS**

| Step | Result |
|------|--------|
| Customer posts comment on unassigned case #20 | ✅ `POST` → 201 (no crash) |
| No `NewCustomerMessage` notification created | ✅ Zero Case#20 notifications found |
| No conversation appears in any agent's list | ✅ Neither agent-001 nor agent-002 see Case#20 |

### Scenario 4: Click conversation → correct case detail
**⚠️ PARTIAL**

| Step | Result |
|------|--------|
| Click conversation in Messages tab → correct URL | ✅ `/cases/19` |
| Case detail loads with correct case data | ✅ Subject, status, priority, assignee, customer all correct |
| Comment thread visible on case detail page | ❌ **MISSING** — no comment section rendered |

### Scenario 5: Notification badge / summary
**✅ PASS (backend) — ⚠️ FRONTEND NOT TESTED**

| Step | Result |
|------|--------|
| `GET /api/notifications/summary` shows correct unread count | ✅ Unread count 11 (includes #40) |
| `NewCustomerMessage` type in recent list | ✅ Shows correct type, title, message |

---

## Gaps Found

### GAP 1: Missing `ConversationReadStates` Table (CRITICAL)
**Severity:** 🔴 Critical — endpoint throws HTTP 500  
**Description:** The `ConversationReadStates` table does not exist in the SQL Server database. The app uses `EnsureCreated()` (not EF migrations), and this table was added in Phase 9 AFTER the initial database creation. `EnsureCreated()` does NOT add new tables to an existing database.  
**Impact:** `GET /api/cases/my-conversations` returns HTTP 500 `SqlException` until the table is manually created.  
**Fix:** Either:
1. Add a DDL script in `database/` to create the missing table: `ConversationReadStates (Id INT IDENTITY PK, AgentUserId NVARCHAR(100), CaseId INT, LastViewedUtc DATETIME2)`, OR
2. Drop and recreate the database (loses seed data), OR
3. Add idempotent table-creation in `Program.cs` startup (like the `EnsureCustomerAccountTable` helper).

### GAP 2: Notification Recipient Filtering Missing (MEDIUM)
**Severity:** 🟡 Medium — notification data leakage between agents  
**Description:** `NotificationService.GetAllAsync()` and `GetSummaryAsync()` filter only by `Channel == InApp` but do NOT filter by the `Recipient` field. When `NotifyNewCustomerMessageAsync` creates a notification with `Recipient = AssignedToUserId`, this recipient is never checked.  
**Impact:** All agents see ALL InApp notifications, including `NewCustomerMessage` alerts meant only for the assigned agent. This leaks customer conversation information to unauthorized agents.  
**Fix:** `GetAllAsync()` and `GetSummaryAsync()` should accept an `agentUserId` parameter (or derive it from the JWT). Filter `NewCustomerMessage` (and potentially other recipient-specific types) by `Recipient == agentUserId OR Recipient IS NULL` (the null check preserves existing `CaseOverdue` InApp alerts that are intentionally agent-agnostic). The controller would need to pass the authenticated user's ID.

### GAP 3: No Comment Thread on Case Detail Page (MEDIUM)
**Severity:** 🟡 Medium — messages tab navigates to a dead-end  
**Description:** The Messages tab correctly navigates to `/cases/{id}` when a conversation is clicked, but `case-detail.component.ts` does NOT render the comment thread. The backend has `GET /api/cases/{id}/comments` and `POST /api/cases/{id}/comments` endpoints, and the frontend `CaseService` has `getComments()` and `postComment()` methods, but the case-detail component never calls them.  
**Impact:** Agents can see the conversation list and click through, but cannot read or reply to the conversation on the case detail page. The "Messages" feature is functionally incomplete.  
**Fix:** Add a "Conversation" / "Comments" section to `case-detail.component.ts` that:
1. Loads comments via `caseService.getComments(id)` on init
2. Displays them in a threaded view (author name, timestamp, staff/customer badge, body)
3. Provides a reply form (calls `caseService.postComment(id, body)`)
4. Optionally marks the conversation as read (see GAP 4)

### GAP 4: No "Mark as Read" Mechanism (MEDIUM)
**Severity:** 🟡 Medium — conversations stay unread forever  
**Description:** There is no API endpoint to update `ConversationReadState`. The `GetMyConversationsAsync` method computes `unread` by comparing `lastComment.CreatedAtUtc > LastViewedUtc`, but there's no way for the frontend to set `LastViewedUtc` to "now" when the agent views a conversation.  
**Impact:** All conversations appear as permanently unread (the unread dot never clears).  
**Fix:** Add a `POST /api/cases/{caseId}/conversations/mark-read` endpoint that:
1. Finds or creates a `ConversationReadState` row for `(agentUserId, caseId)`
2. Sets `LastViewedUtc = DateTime.UtcNow`
3. The frontend should call this when the agent opens a conversation (from Messages tab or case detail)

### GAP 5: Case Auto-Assignment Overrides Admin PUT (LOW)
**Severity:** 🟢 Low — assignment edge case  
**Description:** When `POST /api/customer-portal/cases` creates a new case, the auto-assign logic assigns it to an agent (agent-002/Maria in this test). A subsequent admin `PUT /api/cases/19` with `assignedToUserId: "agent-001"` returned HTTP 204 but the database still shows `agent-002`.  
**Impact:** Admins may not be able to reassign cases that were auto-assigned.  
**Likely cause:** The `PUT` endpoint's `UpdateCaseDto.assignedToUserId` field may not be wired to the update logic, or the field name is different. Needs investigation in `CasesController.Update()` and `CaseService.UpdateAsync()`.

### GAP 6: `EnsureCreated()` Schema Drift Risk (LOW)
**Severity:** 🟢 Low — systemic issue  
**Description:** Every new phase that adds entities/tables requires manual table creation because `EnsureCreated()` never adds columns/tables to an existing database. This is a recurring pattern (ConversationReadStates is the latest example).  
**Fix (recommended):** Migrate to EF Core Migrations for schema management, OR add a startup helper (like `EnsureCustomerAccountTable`) that runs idempotent `IF NOT EXISTS CREATE TABLE` DDL for each new entity.

---

## What Works (Backend)
- ✅ `POST /api/customer-portal/cases/{id}/comments` — customer can post comments
- ✅ `POST /api/cases/{id}/comments` — staff can post comments  
- ✅ `GET /api/cases/{id}/comments` — returns full comment thread with author names
- ✅ `NotifyNewCustomerMessageAsync` — creates `NewCustomerMessage` notification with correct recipient, idempotent, skips unassigned
- ✅ `GET /api/cases/my-conversations` — returns correct conversations with unread flag (once table exists)
- ✅ `GET /api/notifications/summary` — includes `NewCustomerMessage` type in count
- ✅ Conversation data isolation in the conversations list (agent only sees own cases)

## What Works (Frontend)
- ✅ Messages nav link appears for agents (hidden for admin)
- ✅ Messages page loads and displays conversation list
- ✅ Click conversation navigates to correct case detail URL
- ✅ Case detail page loads correct case data
- ✅ Notification bell shows count (2+ in this test, includes CaseOverdue + NewCustomerMessage)

## Test Artifacts
- Backend SQL: `CustomerServiceDb` on `localhost,1433` (user `csadmin`)
- Test customer JWT: `phase9test@example.com` (customer id 19)
- Agent tokens: `$AGENT_TOKEN` (agent-001/Grace), `$MARIA_TOKEN` (agent-002/Maria)
- Test cases: #19 (assigned to agent-002), #20 (unassigned)
- `ConversationReadStates` table manually created for testing
