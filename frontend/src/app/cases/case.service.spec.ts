import { TestBed } from '@angular/core/testing';
import {
  HttpClientTestingModule,
  HttpTestingController,
} from '@angular/common/http/testing';
import { CaseService } from './case.service';
import { Case } from '../shared/models';

/**
 * Tests for CaseService: verifies the correct HTTP verbs/URLs are used and
 * that list filters are serialized into query parameters.
 */
describe('CaseService', () => {
  let service: CaseService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(CaseService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists cases and forwards filters as query params', () => {
    service
      .list({ status: 'New', priority: 'High', categoryId: 2 })
      .subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/cases' &&
        r.params.get('status') === 'New' &&
        r.params.get('priority') === 'High' &&
        r.params.get('categoryId') === '2',
    );
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('gets a single case by id', () => {
    const sample: Case = {
      id: 7,
      subject: 's',
      description: 'd',
      status: 'New',
      priority: 'Low',
      priorityAutoSuggested: false,
      customerId: 1,
      customerName: 'c',
      categoryId: 1,
      categoryName: 'Billing',
      assignedToUserId: null,
      assignedToUserName: null,
      createdAtUtc: '2024-01-01T00:00:00Z',
      updatedAtUtc: null,
      followUpDueUtc: null,
      daysOverdue: null,
    };
    service.get(7).subscribe((c) => expect(c.id).toBe(7));
    const req = httpMock.expectOne('/api/cases/7');
    expect(req.request.method).toBe('GET');
    req.flush(sample);
  });

  it('creates a case with a POST', () => {
    service
      .create({ subject: 'x', description: 'y', categoryId: 1, customerId: 1 })
      .subscribe();
    const req = httpMock.expectOne('/api/cases');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 1 } as Case);
  });

  it('updates a case with a PUT', () => {
    service
      .update(3, {
        subject: 'x',
        description: 'y',
        status: 'Closed',
        priority: 'Low',
        categoryId: 1,
      })
      .subscribe();
    const req = httpMock.expectOne('/api/cases/3');
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('deletes a case with a DELETE', () => {
    service.delete(9).subscribe();
    const req = httpMock.expectOne('/api/cases/9');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('sends the description for sentiment-based AI preview', () => {
    service
      .predictPriority({ categoryId: 1, customerId: 1, description: 'URGENT refund' })
      .subscribe();
    const req = httpMock.expectOne('/api/ml/predict-priority');
    expect(req.request.body.description).toBe('URGENT refund');
    expect(req.request.body.hasComplaintKeyword).toBeUndefined();
    req.flush({ priority: 'High', reason: 'r' });
  });
});
