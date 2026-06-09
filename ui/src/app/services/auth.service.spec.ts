import { TestBed } from '@angular/core/testing';
import {
  provideHttpClient,
  withInterceptors
} from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController
} from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

const API = `${environment.apiUrl}/auth`;

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const tokenResponse = (token: string) => ({
    success: true,
    message: 'OK',
    data: { token }
  });

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  // ─── Initial state ─────────────────────────────────────────────────────────

  it('isLoggedIn() returns false when no token is stored', () => {
    expect(service.isLoggedIn()).toBe(false);
    expect(service.getToken()).toBeNull();
  });

  // ─── Login ─────────────────────────────────────────────────────────────────

  it('login() POSTs to /auth/login with email and password', () => {
    service.login({ email: 'user@example.com', password: 'P@ss' }).subscribe();

    const req = httpMock.expectOne(`${API}/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.email).toBe('user@example.com');
    expect(req.request.body.password).toBe('P@ss');
    req.flush(tokenResponse('t'));
  });

  it('login() stores token in localStorage on success', () => {
    service.login({ email: 'user@example.com', password: 'pass' }).subscribe(res => {
      expect(res.token).toBe('my-token');
      expect(service.getToken()).toBe('my-token');
      expect(service.isLoggedIn()).toBe(true);
    });

    const req = httpMock.expectOne(`${API}/login`);
    req.flush(tokenResponse('my-token'));
  });

  it('login() throws when server returns success=false', (done: any) => {
    service.login({ email: 'user@example.com', password: 'pass' }).subscribe({
      error: (err) => {
        expect(err.message).toBeTruthy();
        expect(service.isLoggedIn()).toBe(false);
        done();
      }
    });

    const req = httpMock.expectOne(`${API}/login`);
    req.flush({ success: false, message: 'Invalid credentials', data: null });
  });

  it('login() throws and does not store token on HTTP 401', (done: any) => {
    service.login({ email: 'bad@example.com', password: 'wrong' }).subscribe({
      error: () => {
        expect(service.getToken()).toBeNull();
        done();
      }
    });

    const req = httpMock.expectOne(`${API}/login`);
    req.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
  });

  // ─── Register ──────────────────────────────────────────────────────────────

  it('register() POSTs to /auth/register and stores token on success', () => {
    service.register({ email: 'new@example.com', password: 'P@ss123' }).subscribe(res => {
      expect(res.token).toBe('reg-token');
      expect(service.getToken()).toBe('reg-token');
    });

    const req = httpMock.expectOne(`${API}/register`);
    expect(req.request.method).toBe('POST');
    req.flush(tokenResponse('reg-token'));
  });

  it('register() throws when email is already taken (success=false)', (done: any) => {
    service.register({ email: 'taken@example.com', password: 'pass' }).subscribe({
      error: (err) => {
        expect(err.message).toBeTruthy();
        done();
      }
    });

    const req = httpMock.expectOne(`${API}/register`);
    req.flush({ success: false, message: 'Email already exists', data: null });
  });

  // ─── OTP ──────────────────────────────────────────────────────────────────

  it('sendOtp() POSTs to /auth/otp/send', () => {
    service.sendOtp({ mobileNumber: '+919876543210' }).subscribe();

    const req = httpMock.expectOne(`${API}/otp/send`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.mobileNumber).toBe('+919876543210');
    req.flush({ success: true, message: 'OTP sent' });
  });

  it('verifyOtp() POSTs to /auth/otp/verify and stores token on success', () => {
    service.verifyOtp({ mobileNumber: '+919876543210', otp: '123456' }).subscribe(res => {
      expect(res.token).toBe('otp-token');
      expect(service.isLoggedIn()).toBe(true);
    });

    const req = httpMock.expectOne(`${API}/otp/verify`);
    expect(req.request.method).toBe('POST');
    req.flush(tokenResponse('otp-token'));
  });

  it('verifyOtp() throws and does not log in on wrong OTP', (done: any) => {
    service.verifyOtp({ mobileNumber: '+919876543210', otp: '000000' }).subscribe({
      error: () => {
        expect(service.isLoggedIn()).toBe(false);
        done();
      }
    });

    const req = httpMock.expectOne(`${API}/otp/verify`);
    req.flush({ success: false, message: 'Invalid OTP', data: null });
  });

  // ─── Google login ──────────────────────────────────────────────────────────

  it('googleLogin() POSTs credential to /auth/google and stores token', () => {
    service.googleLogin('google-credential-token').subscribe(res => {
      expect(res.token).toBe('google-jwt');
      expect(service.isLoggedIn()).toBe(true);
    });

    const req = httpMock.expectOne(`${API}/google`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.credential).toBe('google-credential-token');
    req.flush(tokenResponse('google-jwt'));
  });

  // ─── Logout ───────────────────────────────────────────────────────────────

  it('logout() clears token from localStorage', () => {
    localStorage.setItem('auth_token', 'some-token');
    expect(service.isLoggedIn()).toBe(true);

    service.logout();

    expect(service.getToken()).toBeNull();
    expect(service.isLoggedIn()).toBe(false);
  });

  it('logout() is idempotent — calling twice does not throw', () => {
    service.logout();
    expect(() => service.logout()).not.toThrow();
    expect(service.isLoggedIn()).toBe(false);
  });

  // ─── Session state after login then logout ─────────────────────────────────

  it('isLoggedIn() returns false after logout following a successful login', () => {
    // Login
    service.login({ email: 'u@x.com', password: 'p' }).subscribe();
    httpMock.expectOne(`${API}/login`).flush(tokenResponse('abc'));

    expect(service.isLoggedIn()).toBe(true);

    // Logout
    service.logout();
    expect(service.isLoggedIn()).toBe(false);
    expect(service.getToken()).toBeNull();
  });

  // ─── getProfile ───────────────────────────────────────────────────────────

  it('getProfile() GETs /auth/profile and returns profile data', () => {
    service.getProfile().subscribe(profile => {
      expect(profile.email).toBe('user@example.com');
      expect(profile.userId).toBe(5);
    });

    const req = httpMock.expectOne(`${API}/profile`);
    expect(req.request.method).toBe('GET');
    req.flush({
      success: true,
      message: 'OK',
      data: {
        userId: 5,
        email: 'user@example.com',
        mobileNumber: null,
        isEmailVerified: true,
        isMobileVerified: false,
        createdAtUtc: '2026-01-01T00:00:00Z'
      }
    });
  });

  it('getProfile() throws when success=false', (done: any) => {
    service.getProfile().subscribe({
      error: (err) => {
        expect(err.message).toBeTruthy();
        done();
      }
    });

    const req = httpMock.expectOne(`${API}/profile`);
    req.flush({ success: false, message: 'Not found', data: null });
  });
});