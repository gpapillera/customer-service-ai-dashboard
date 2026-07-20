/** Shared TypeScript models mirroring the backend DTOs. */

/** Request payload for POST /api/auth/login. */
export interface LoginRequest {
  userName: string;
  password: string;
}

/** Response from POST /api/auth/login. */
export interface LoginResponse {
  token: string;
  expiresUtc: string;
  userName: string;
  fullName: string;
  role: string;
}

/** A customer record (matches CustomerDto). */
export interface Customer {
  id: number;
  name: string;
  email: string;
  phone: string | null;
  company: string | null;
  address: string | null;
  caseCount: number;
  createdAtUtc?: string;
}

/** Payload for creating a customer. */
export interface CreateCustomer {
  name: string;
  email: string;
  phone?: string | null;
  company?: string | null;
  address?: string | null;
}

/** A support case (matches CaseDto). */
export interface Case {
  id: number;
  subject: string;
  description: string;
  status: 'New' | 'InProgress' | 'Escalated' | 'Resolved' | 'Closed';
  priority: 'Low' | 'Medium' | 'High';
  priorityAutoSuggested: boolean;
  priorityReason?: string | null;
  customerId: number;
  customerName: string;
  categoryId: number;
  categoryName: string;
  assignedToUserId: string | null;
  assignedToUserName: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  followUpDueUtc: string | null;
  /** Days past the follow-up reference point when overdue; null otherwise. */
  daysOverdue: number | null;
}

/** Payload for creating a case. */
export interface CreateCase {
  subject: string;
  description: string;
  categoryId: number;
  customerId: number;
  assignedToUserId?: string | null;
  priority?: 'Low' | 'Medium' | 'High' | null;
  lastContactUtc?: string | null;
}

/** Payload for updating a case. */
export interface UpdateCase {
  subject: string;
  description: string;
  status: 'New' | 'InProgress' | 'Escalated' | 'Resolved' | 'Closed';
  priority: 'Low' | 'Medium' | 'High';
  categoryId: number;
  assignedToUserId?: string | null;
}

/** A call / follow-up log tied to a case (matches CallLogDto). */
export interface CallLog {
  id: number;
  caseId: number;
  direction: 'Inbound' | 'Outbound';
  notes: string;
  durationSeconds: number;
  loggedByUserId: string | null;
  createdAtUtc: string;
}

/** Payload for creating a call log. */
export interface CreateCallLog {
  caseId: number;
  direction: 'Inbound' | 'Outbound';
  notes: string;
  durationSeconds: number;
}

/** A category lookup. */
export interface Category {
  id: number;
  name: string;
  description: string | null;
}

/** A lightweight user summary for assignment dropdowns (matches AgentSummary). */
export interface Agent {
  id: string;
  fullName: string;
  role: 'Admin' | 'Agent';
  /** Open (not Resolved/Closed) cases currently assigned to this agent. */
  openCaseCount: number;
}

/** A single point in a dashboard trend series. */
export interface DateCount {
  date: string;
  count: number;
}

// ---- Customer portal DTOs (Phase 3) ----
// These deliberately mirror the backend's CustomerPortalDtos, which omit
// priority / AI-prediction / call-log / assigned-agent fields by design.

/** Customer-facing case list item. */
export interface CustomerCaseSummary {
  id: number;
  subject: string;
  status: 'New' | 'InProgress' | 'Escalated' | 'Resolved' | 'Closed';
  createdAtUtc: string;
}

/** Customer-facing case detail. */
export interface CustomerCaseDetail {
  id: number;
  subject: string;
  description: string;
  status: 'New' | 'InProgress' | 'Escalated' | 'Resolved' | 'Closed';
  createdAtUtc: string;
  resolvedAtUtc: string | null;
  comments: CustomerCaseComment[];
}

/** A comment on the shared customer/staff thread. */
export interface CustomerCaseComment {
  id: number;
  authorDisplayName: string;
  isStaff: boolean;
  body: string;
  createdAtUtc: string;
}

/** Payload for a customer to create a case. */
export interface CreateCustomerCase {
  subject: string;
  description: string;
  categoryId: number;
}

/** Payload for a customer to post a comment. */
export interface CreateCustomerComment {
  body: string;
}

/** Response from GET /api/customer-auth/validate-invite. */
export interface ValidateInviteResponse {
  valid: boolean;
  customerName: string | null;
  customerEmailMasked: string | null;
}

/** A category/count pair for breakdown charts. */
export interface CategoryCount {
  category: string;
  count: number;
}

/** A recent case summary for the dashboard list. */
export interface RecentCase {
  id: number;
  subject: string;
  customerName: string;
  categoryName: string;
  createdAtUtc: string;
  priority: 'Low' | 'Medium' | 'High';
  status: 'New' | 'InProgress' | 'Escalated' | 'Resolved' | 'Closed';
  priorityAutoSuggested: boolean;
}

/** A case with an overdue follow-up (matches OverdueFollowUpDto). */
export interface OverdueFollowUp {
  caseId: number;
  subject: string;
  customerName: string;
  assignedToUserName: string;
  priority: 'Low' | 'Medium' | 'High';
  followUpDueUtc: string;
  daysOverdue: number;
}

/** Full dashboard payload (matches DashboardDto). */
export interface Dashboard {
  totalCases: number;
  openCases: number;
  closedCases: number;
  resolvedCases: number;
  aiPredictedCases: number;
  highPriorityCases: number;
  totalCustomers: number;
  /** Agent-scoped totals (populated only for an Agent caller). */
  myCases: number;
  myOpenCases: number;
  myHighPriorityCases: number;
  myAiPredictedCases: number;
  myResolvedCases: number;
  myOverdueFollowUps: number;
  byStatus: Record<string, number>;
  byPriority: Record<string, number>;
  trend: DateCount[];
  byCategory: CategoryCount[];
  recentCases: RecentCase[];
  overdueFollowUps: number;
  overdueFollowUpsList: OverdueFollowUp[];
}

/** A notification shown in the in-app notification center. */
export interface Notification {
  id: number;
  title: string;
  message: string;
  channel: 'InApp' | 'Email' | 'Sms';
  status: 'Unread' | 'Read';
  createdAtUtc: string;
  link: string | null;
  caseId: number | null;
}

/** Summary for the bell badge (unread count + recent preview). */
export interface NotificationSummary {
  unreadCount: number;
  recent: Notification[];
}

/**
 * A currently-overdue case surfaced in the notification center. Derived live
 * from the cases API (overdue filter), not from stored notification rows, so
 * it persists for as long as the case remains overdue.
 */
export interface OverdueCase {
  caseId: number;
  subject: string;
  customerName: string;
  assignedToUserName: string;
  priority: 'Low' | 'Medium' | 'High';
  followUpDueUtc: string;
  daysOverdue: number;
  /** Full case record (for the expanded detail view). */
  detail: Case;
}
