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
 * See docs/DIY.md §4 — registered in app.config.ts via HTTP_INTERCEPTORS.
 */
@Injectable()
export class TokenInterceptor implements HttpInterceptor {
  constructor(private readonly auth: AuthService) {}

  intercept(
    req: HttpRequest<unknown>,
    next: HttpHandler,
  ): Observable<HttpEvent<unknown>> {
    const token = this.auth.getToken();
    const authReq = token
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;

    return next.handle(authReq);
  }
}
