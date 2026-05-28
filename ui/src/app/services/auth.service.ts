import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface AuthResponseDto {
  token: string;
  email: string;
  mobileNumber?: string | null;
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
  mobileNumber?: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface SendOtpRequest {
  mobileNumber: string;
}

export interface VerifyOtpRequest {
  mobileNumber: string;
  otp: string;
}

export interface UserProfileDto {
  userId: number;
  email: string;
  mobileNumber?: string | null;
  isEmailVerified: boolean;
  isMobileVerified: boolean;
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
}

export interface UpdateUserProfileRequest {
  email: string;
  mobileNumber?: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';
  private readonly USER_KEY = 'auth_user';

  constructor(private http: HttpClient) {}

  register(data: RegisterRequest): Observable<AuthResponseDto> {
    return this.handleAuth(this.http.post<ApiResponse<AuthResponseDto>>(`${this.apiUrl}/register`, data));
  }

  login(data: LoginRequest): Observable<AuthResponseDto> {
    return this.handleAuth(this.http.post<ApiResponse<AuthResponseDto>>(`${this.apiUrl}/login`, data));
  }

  googleLogin(credential: string): Observable<AuthResponseDto> {
    return this.handleAuth(this.http.post<ApiResponse<AuthResponseDto>>(`${this.apiUrl}/google`, { credential }));
  }

  sendOtp(data: SendOtpRequest): Observable<any> {
  return this.http.post(`${this.apiUrl}/otp/send`, data);
}

  verifyOtp(data: VerifyOtpRequest): Observable<AuthResponseDto> {
    return this.handleAuth(this.http.post<ApiResponse<AuthResponseDto>>(`${this.apiUrl}/otp/verify`, data));
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


  getProfile(): Observable<UserProfileDto> {
  return this.http
    .get<ApiResponse<UserProfileDto>>(`${this.apiUrl}/profile`)
    .pipe(
      map(res => {
        if (!res.success || !res.data) {
          throw new Error(res.message || 'Failed to fetch profile');
        }

        return res.data;
      })
    );
}

updateProfile(data: UpdateUserProfileRequest): Observable<UserProfileDto> {
  return this.http
    .put<ApiResponse<UserProfileDto>>(`${this.apiUrl}/profile`, data)
    .pipe(
      map(res => {
        if (!res.success || !res.data) {
          throw new Error(res.message || 'Failed to update profile');
        }

        const currentUser = this.getCurrentUser();

        if (currentUser) {
          localStorage.setItem(
            this.USER_KEY,
            JSON.stringify({
              ...currentUser,
              email: res.data.email,
              mobileNumber: res.data.mobileNumber
            })
          );
        }

        return res.data;
      })
    );
}

changePassword(data: ChangePasswordRequest): Observable<ApiResponse<object>> {
  return this.http.put<ApiResponse<object>>(`${this.apiUrl}/change-password`, data);
}

  private handleAuth(source$: Observable<ApiResponse<AuthResponseDto>>): Observable<AuthResponseDto> {
    return source$.pipe(
      map(res => {
        if (!res.success || !res.data) {
          throw new Error(res.message || 'Authentication failed');
        }
        this.saveSession(res.data);
        return res.data;
      })
    );
  }

  private saveSession(data: AuthResponseDto): void {
    localStorage.setItem(this.TOKEN_KEY, data.token);
    localStorage.setItem(this.USER_KEY, JSON.stringify(data));
  }
}
