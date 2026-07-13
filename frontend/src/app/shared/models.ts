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

/** A single point in a dashboard trend series. */
export interface DateCount {
  date: string;
  count: number;
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

/** Full dashboard payload (matches DashboardDto). */
export interface Dashboard {
  totalCases: number;
  openCases: number;
  closedCases: number;
  resolvedCases: number;
  aiPredictedCases: number;
  highPriorityCases: number;
  totalCustomers: number;
  byStatus: Record<string, number>;
  byPriority: Record<string, number>;
  trend: DateCount[];
  byCategory: CategoryCount[];
  recentCases: RecentCase[];
}
