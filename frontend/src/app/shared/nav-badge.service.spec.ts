import { Case } from './models';

/**
 * Mirrors the new-cases-since-visit predicate inside NavBadgeService.refresh().
 * Kept in sync by hand: if the service's counting logic changes, update this
 * fixture-based check so the "assigned to me since I last looked" math stays
 * honest without needing a full Angular TestBed.
 */
function newCasesSince(
  cases: Case[],
  userId: string | null,
  sinceMs: number,
): number {
  if (!sinceMs) return 0;
  return cases.filter((c) => {
    const created = new Date(c.createdAtUtc).getTime();
    const assigned = (c.assignedToUserId === userId && c.assignedAtUtc)
      ? new Date(c.assignedAtUtc).getTime()
      : -1;
    return created > sinceMs || assigned > sinceMs;
  }).length;
}

/** Mirrors NavBadgeService.refresh() newCustomersSince — created OR updated since visit. */
function newCustomersSince(
  customers: { createdAtUtc?: string; updatedAtUtc?: string | null }[],
  sinceMs: number,
): number {
  if (!sinceMs) return 0;
  return customers.filter((cu) => {
    const created = cu.createdAtUtc ? new Date(cu.createdAtUtc).getTime() : -1;
    const updated = cu.updatedAtUtc ? new Date(cu.updatedAtUtc).getTime() : -1;
    return created > sinceMs || updated > sinceMs;
  }).length;
}

function baseCase(over: Partial<Case>): Case {
  return {
    id: 1,
    caseDisplayId: 'CAS-1',
    subject: 's',
    description: 'd',
    status: 'New',
    priority: 'Low',
    priorityAutoSuggested: false,
    customerId: 1,
    customerName: 'c',
    categoryId: 1,
    categoryName: 'cat',
    assignedToUserId: null,
    assignedToUserName: null,
    createdAtUtc: '',
    updatedAtUtc: null,
    followUpDueUtc: null,
    daysOverdue: null,
    commentCount: 0,
    ...over,
  };
}

describe('nav badge new-case predicate', () => {
  const since = Date.parse('2026-01-01T00:00:00Z');

  it('counts cases created after the visit', () => {
    const list = [
      baseCase({ createdAtUtc: '2026-02-01T00:00:00Z' }),
      baseCase({ createdAtUtc: '2025-12-01T00:00:00Z' }),
    ];
    expect(newCasesSince(list, 'agent-1', since)).toBe(1);
  });

  it('counts cases assigned to me after the visit (even if created earlier)', () => {
    const list = [
      baseCase({ createdAtUtc: '2025-12-01T00:00:00Z', assignedToUserId: 'agent-1', assignedAtUtc: '2026-02-01T00:00:00Z' }),
      baseCase({ createdAtUtc: '2025-12-01T00:00:00Z', assignedToUserId: 'agent-2', assignedAtUtc: '2026-02-01T00:00:00Z' }),
    ];
    expect(newCasesSince(list, 'agent-1', since)).toBe(1);
  });

  it('does not double-count a case both created and assigned after the visit', () => {
    const list = [
      baseCase({ createdAtUtc: '2026-02-01T00:00:00Z', assignedToUserId: 'agent-1', assignedAtUtc: '2026-02-02T00:00:00Z' }),
    ];
    expect(newCasesSince(list, 'agent-1', since)).toBe(1);
  });

  it('returns 0 when there is no baseline (first visit)', () => {
    const list = [ baseCase({ createdAtUtc: '2026-02-01T00:00:00Z' }) ];
    expect(newCasesSince(list, 'agent-1', 0)).toBe(0);
  });
});

describe('nav badge new-customer predicate', () => {
  const since = Date.parse('2026-01-01T00:00:00Z');

  it('counts customers created after the visit', () => {
    const list = [
      { createdAtUtc: '2026-02-01T00:00:00Z', updatedAtUtc: null },
      { createdAtUtc: '2025-12-01T00:00:00Z', updatedAtUtc: null },
    ];
    expect(newCustomersSince(list, since)).toBe(1);
  });

  it('counts customers whose profile was updated after the visit', () => {
    const list = [
      { createdAtUtc: '2025-12-01T00:00:00Z', updatedAtUtc: '2026-02-01T00:00:00Z' },
      { createdAtUtc: '2025-12-01T00:00:00Z', updatedAtUtc: null },
    ];
    expect(newCustomersSince(list, since)).toBe(1);
  });

  it('does not double-count a customer both created and updated after the visit', () => {
    const list = [
      { createdAtUtc: '2026-02-01T00:00:00Z', updatedAtUtc: '2026-02-02T00:00:00Z' },
    ];
    expect(newCustomersSince(list, since)).toBe(1);
  });

  it('returns 0 when there is no baseline (first visit)', () => {
    const list = [{ createdAtUtc: '2026-02-01T00:00:00Z', updatedAtUtc: null }];
    expect(newCustomersSince(list, 0)).toBe(0);
  });
});
