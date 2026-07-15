import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DashboardComponent } from './dashboard.component';
import { DashboardService } from './dashboard.service';
import { Dashboard } from '../shared/models';

/**
 * Tests for the Dashboard component: verifies KPI derivation and that the
 * status chart always shows all five statuses (even at count 0).
 */
describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let service: DashboardService;
  let httpMock: HttpTestingController;

  const sample: Dashboard = {
    totalCases: 13,
    openCases: 11,
    closedCases: 2,
    resolvedCases: 2,
    aiPredictedCases: 5,
    highPriorityCases: 5,
    totalCustomers: 11,
    byStatus: { InProgress: 1, Escalated: 1, Resolved: 1, Closed: 1 },
    byPriority: { Low: 3, Medium: 5, High: 5 },
    trend: [{ date: '2026-07-13', count: 1 }],
    byCategory: [{ category: 'Billing', count: 4 }],
    recentCases: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    component = TestBed.createComponent(DashboardComponent).componentInstance;
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('derives the six KPI cards from the payload', () => {
    component.data.set(sample);
    const labels = component.kpis.map((k) => k.label);
    expect(labels).toEqual([
      'Total Cases',
      'Open Cases',
      'High Priority',
      'Resolved',
      'Customers',
      'AI Predicted',
    ]);
    expect(component.kpis.find((k) => k.label === 'Total Cases')?.value).toBe(13);
  });

  it('always renders all five statuses, including zero-count "New"', () => {
    component.data.set(sample);
    const byStatus = sample.byStatus;
    const statusOrder = ['New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
    const data = statusOrder.map((s) => byStatus[s] ?? 0);
    expect(data).toEqual([0, 1, 1, 1, 1]);
  });

  it('loads the dashboard from the API on init', () => {
    component.ngOnInit();
    const req = httpMock.expectOne('/api/dashboard');
    req.flush(sample);
    expect(component.data()?.totalCases).toBe(13);
    expect(component.loading()).toBeFalse();
  });
});
