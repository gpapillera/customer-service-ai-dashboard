import { Injectable } from '@angular/core';
import {
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpEvent,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Attaches the JWT bearer token to every outgoing API request and redirects to
 * /login when the API responds with 401 Unauthorized.
 *
 * It deliberately skips /api/customer-portal requests — those are the customer
 * portal's domain and are authenticated by the separate CustomerTokenInterceptor
 * with the customer JWT. This keeps the two token scopes from colliding on the
 * same request. See docs/DIY.md §4 and §8.
 */
@Injectable()
export class TokenInterceptor implements HttpInterceptor {
  constructor(private readonly auth: AuthService) {}

  intercept(
    req: HttpRequest<unknown>,
    next: HttpHandler,
  ): Observable<HttpEvent<unknown>> {
    if (req.url.startsWith('/api/customer-portal')) {
      return next.handle(req);
    }
    const token = this.auth.getToken();
    const authReq = token
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;

    return next.handle(authReq);
  }
}
