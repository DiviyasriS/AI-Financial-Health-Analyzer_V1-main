import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface AuthResponseDto {
  token: string;
  email: string;
  userId: number;
}

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T | null;
}

export interface RegisterRequest {
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {

  private readonly apiUrl = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';
  private readonly USER_KEY  = 'auth_user';

  constructor(private http: HttpClient) {}

  register(data: RegisterRequest): Observable<AuthResponseDto> {
    return this.http
      .post<ApiResponse<AuthResponseDto>>(`${this.apiUrl}/register`, data)
      .pipe(
        map(res => {
          if (!res.success || !res.data) {
            throw new Error(res.message || 'Registration failed');
          }
          this.saveSession(res.data);
          return res.data;
        })
      );
  }

  login(data: LoginRequest): Observable<AuthResponseDto> {
    return this.http
      .post<ApiResponse<AuthResponseDto>>(`${this.apiUrl}/login`, data)
      .pipe(
        map(res => {
          if (!res.success || !res.data) {
            throw new Error(res.message || 'Login failed');
          }
          this.saveSession(res.data);
          return res.data;
        })
      );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getCurrentUser(): AuthResponseDto | null {
    const stored = localStorage.getItem(this.USER_KEY);
    return stored ? (JSON.parse(stored) as AuthResponseDto) : null;
  }

  private saveSession(data: AuthResponseDto): void {
    localStorage.setItem(this.TOKEN_KEY, data.token);
    localStorage.setItem(this.USER_KEY, JSON.stringify(data));
  }
}