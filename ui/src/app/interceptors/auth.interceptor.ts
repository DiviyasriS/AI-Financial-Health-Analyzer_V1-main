import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

// FIX: Added 401 handling.
// Previously: expired or invalid tokens caused silent 401 errors in the UI.
// Now: any 401 response automatically logs the user out and redirects to /login.

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router      = inject(Router);
  const token       = authService.getToken();

  // Clone request and attach Bearer token if available
  const authReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        // Token expired or invalid — clear session and redirect to login
        authService.logout();
        router.navigate(['/login']);
      }
      return throwError(() => error);
    })
  );
};