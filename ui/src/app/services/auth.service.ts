import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { tap, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';

// FIX: These interfaces now match what the backend actually returns.
// AuthController now returns a flat AuthResponseDto (not wrapped in ApiResponse).
// Shape: { token: string, email: string, userId: number }
export interface AuthResponse {
  token:  string;
  email:  string;
  userId: number;
}

export interface RegisterRequest {
  email:    string;
  password: string;
}

export interface LoginRequest {
  email:    string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {

  private readonly apiUrl    = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';
  private readonly USER_KEY  = 'auth_user';

  constructor(private http: HttpClient) {}

  // ── Register ────────────────────────────────────────────────────────────────

  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, data).pipe(
      tap(response => this.saveSession(response)),
      catchError(err => {
        // Clear any stale session on auth error
        this.clearSession();
        return throwError(() => err);
      })
    );
  }

  // ── Login ───────────────────────────────────────────────────────────────────

  login(data: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, data).pipe(
      tap(response => this.saveSession(response)),
      catchError(err => {
        this.clearSession();
        return throwError(() => err);
      })
    );
  }

  // ── Logout ──────────────────────────────────────────────────────────────────

  logout(): void {
    this.clearSession();
  }

  // ── Session helpers ─────────────────────────────────────────────────────────

  isLoggedIn(): boolean {
    const token = this.getToken();
    if (!token) return false;

    // FIX: Also check the token has not expired client-side.
    // This prevents sending an expired token that will get a 401 from the server.
    try {
      const payload    = JSON.parse(atob(token.split('.')[1]));
      const expSeconds = payload.exp as number;
      if (expSeconds && Date.now() / 1000 > expSeconds) {
        this.clearSession();
        return false;
      }
    } catch {
      // Malformed token — treat as logged out
      this.clearSession();
      return false;
    }

    return true;
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getCurrentUser(): AuthResponse | null {
    const stored = localStorage.getItem(this.USER_KEY);
    if (!stored) return null;
    try {
      return JSON.parse(stored) as AuthResponse;
    } catch {
      return null;
    }
  }

  // ── Private helpers ─────────────────────────────────────────────────────────

  private saveSession(response: AuthResponse): void {
    // Guard: verify the response actually has a token before saving.
    // If backend sends a wrapped ApiResponse by mistake, response.token would be
    // undefined and we'd store the string "undefined" — which would silently break auth.
    if (!response?.token) {
      console.error('AuthService.saveSession: response has no token!', response);
      return;
    }
    localStorage.setItem(this.TOKEN_KEY, response.token);
    localStorage.setItem(this.USER_KEY,  JSON.stringify(response));
  }

  private clearSession(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
  }
}