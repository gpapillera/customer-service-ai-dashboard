import { TestBed } from '@angular/core/testing';
import {
  HttpClientTestingModule,
  HttpTestingController,
} from '@angular/common/http/testing';
import { HTTP_INTERCEPTORS, HttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { TokenInterceptor } from './token.interceptor';

/**
 * Tests for the JWT interceptor: it must attach the bearer token to outgoing
 * requests and leave requests untouched when no token is present.
 */
describe('TokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        { provide: HTTP_INTERCEPTORS, useClass: TokenInterceptor, multi: true },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => httpMock.verify());

  it('attaches the bearer token when authenticated', () => {
    spyOn(auth, 'getToken').and.returnValue('test-jwt');
    http.get('/api/dashboard').subscribe();
    const req = httpMock.expectOne('/api/dashboard');
    expect(req.request.headers.get('Authorization')).toBe('Bearer test-jwt');
  });

  it('sends no Authorization header when there is no token', () => {
    spyOn(auth, 'getToken').and.returnValue(null);
    http.get('/api/dashboard').subscribe();
    const req = httpMock.expectOne('/api/dashboard');
    expect(req.request.headers.has('Authorization')).toBeFalse();
  });
});
