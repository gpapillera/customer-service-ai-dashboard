import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { CaseTableSettingsService, CASE_COLUMNS } from './case-table-settings.service';
import { AuthService } from '../auth/auth.service';
import { LoginResponse } from '../shared/models';

/**
 * Build a FRESH service instance (singletons are cached per TestBed injector,
 * so we reset the module first). The desired user is applied BEFORE the service
 * is constructed, so its constructor-time load() reads the correct storage key.
 */
function makeService(userName: string | null): CaseTableSettingsService {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
  const auth = TestBed.inject(AuthService);
  if (userName) {
    (auth.currentUser as any).set({ userName, role: 'Admin', fullName: userName } as LoginResponse);
  } else {
    (auth.currentUser as any).set(null);
  }
  return TestBed.inject(CaseTableSettingsService);
}

describe('CaseTableSettingsService', () => {
  afterEach(() => localStorage.clear());

  it('defaults to all columns in canonical order with no widths', () => {
    const svc = makeService(null);
    expect(svc.columnOrder()).toEqual([...CASE_COLUMNS]);
    expect(svc.columnWidths()).toEqual({});
  });

  it('includes assignedToUserId as 2nd-to-last column by default', () => {
    const svc = makeService(null);
    const order = svc.columnOrder();
    expect(order).toContain('assignedToUserId');
    expect(order[order.length - 1]).toBe('updatedAtUtc');
    expect(order[order.length - 2]).toBe('assignedToUserId');
  });

  it('persists reorder + widths and reloads them for the same user', () => {
    const svc = makeService(null);
    const reordered = ['status', 'priority', ...CASE_COLUMNS.filter(c => c !== 'status' && c !== 'priority')];
    svc.columnOrder.set(reordered);
    svc.setWidth('status', 200);
    svc.setWidth('priority', 120);
    // Reload from storage (same key, no user).
    const reloaded = makeService(null);
    expect(reloaded.columnOrder()[0]).toBe('status');
    expect(reloaded.columnWidths()['status']).toBe(200);
    expect(reloaded.columnWidths()['priority']).toBe(120);
  });

  it('appends newly-added columns missing from a stored order', () => {
    localStorage.setItem('cs-case-cols', JSON.stringify({ order: ['status', 'subject'], widths: {} }));
    const svc = makeService(null);
    const order = svc.columnOrder();
    expect(order[0]).toBe('status'); // stored order preserved first
    expect(order).toContain('priority');
    expect(order.length).toBe(CASE_COLUMNS.length); // no column dropped
    expect(order.indexOf('status')).toBeLessThan(order.indexOf('priority'));
  });

  it('drops unknown / invalid widths and keeps valid ones', () => {
    localStorage.setItem('cs-case-cols', JSON.stringify({
      order: [...CASE_COLUMNS],
      widths: { subject: 30, priority: 150, bogus: 999 }, // 30 < MIN, bogus unknown
    }));
    const svc = makeService(null);
    expect(svc.columnWidths()['subject']).toBeUndefined();
    expect(svc.columnWidths()['priority']).toBe(150);
    expect(svc.columnWidths()['bogus']).toBeUndefined();
  });

  it('reset() clears order and widths', () => {
    const svc = makeService(null);
    svc.setWidth('status', 200);
    svc.reset();
    expect(svc.columnWidths()).toEqual({});
    expect(svc.columnOrder()).toEqual([...CASE_COLUMNS]);
  });

  it('scopes storage per user so layouts do not bleed across users', () => {
    const admin = makeService('admin');
    admin.columnOrder.set(['status', ...CASE_COLUMNS.filter(c => c !== 'status')]);
    admin.setWidth('status', 220);
    expect(JSON.parse(localStorage.getItem('cs-case-cols-admin')!).order[0]).toBe('status');

    const agent = makeService('agent');
    // Agent starts fresh — admin's order/width did not carry over.
    expect(agent.columnOrder()).toEqual([...CASE_COLUMNS]);
    expect(agent.columnWidths()).toEqual({});
    expect(localStorage.getItem('cs-case-cols-agent')).toBeNull();
  });
});
