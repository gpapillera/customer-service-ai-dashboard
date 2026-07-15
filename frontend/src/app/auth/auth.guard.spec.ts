import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

/**
 * Tests for the route guard: it must allow authenticated users through and
 * redirect unauthenticated users to /login.
 */
describe('authGuard', () => {
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { createUrlTree: (c: unknown) => c } },
      ],
    });
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  it('returns true when authenticated', () => {
    spyOn(auth, 'isAuthenticated').and.returnValue(true);
    expect(TestBed.runInInjectionContext(() => authGuard({} as never, {} as never))).toBe(true);
  });

  it('redirects to /login when not authenticated', () => {
    spyOn(auth, 'isAuthenticated').and.returnValue(false);
    const createUrlTree = spyOn(router, 'createUrlTree').and.callThrough();
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(createUrlTree).toHaveBeenCalledWith(['/login']);
    expect(Array.isArray(result)).toBeTrue();
  });
});
