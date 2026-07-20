import { Injectable } from '@angular/core';
import {
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpEvent,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { CustomerAuthService } from './customer-auth.service';

/**
 * Attaches the CUSTOMER JWT bearer token, but ONLY to requests whose URL starts
 * with /api/customer-portal. All other requests (staff API, public auth) are
 * left untouched so this interceptor never fights the staff TokenInterceptor.
 *
 * Registered alongside TokenInterceptor in app.config.ts. The two are mutually
 * exclusive by URL prefix: staff tokens go to everything except
 * /api/customer-portal, customer tokens go only to /api/customer-portal.
 */
@Injectable()
export class CustomerTokenInterceptor implements HttpInterceptor {
  constructor(private readonly auth: CustomerAuthService) {}

  intercept(
    req: HttpRequest<unknown>,
    next: HttpHandler,
  ): Observable<HttpEvent<unknown>> {
    if (!req.url.startsWith('/api/customer-portal')) {
      return next.handle(req);
    }
    const token = this.auth.getToken();
    const authReq = token
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;
    return next.handle(authReq);
  }
}
