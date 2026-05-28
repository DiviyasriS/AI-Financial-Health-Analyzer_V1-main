import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AuthService, AuthResponseDto } from './auth.service';
import { environment } from '../../environments/environment';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const authResponse: AuthResponseDto = {
    token: 'test-token',
    email: 'test@example.com',
    userId: 1,
    mobileNumber: '+919876543210'
  };

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

  it('should login and save token/user in localStorage', () => {
    service.login({
      email: 'test@example.com',
      password: 'Password@123'
    }).subscribe(result => {
      expect(result.token).toBe('test-token');
      expect(service.getToken()).toBe('test-token');
      expect(service.isLoggedIn()).toBe(true);
      expect(service.getCurrentUser()?.email).toBe('test@example.com');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
    expect(req.request.method).toBe('POST');
    req.flush({
      success: true,
      message: 'Login successful',
      data: authResponse
    });
  });

  it('should register and save session', () => {
    service.register({
      email: 'new@example.com',
      password: 'Password@123'
    }).subscribe(result => {
      expect(result.email).toBe('test@example.com');
      expect(service.getToken()).toBe('test-token');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/register`);
    expect(req.request.method).toBe('POST');
    req.flush({
      success: true,
      message: 'Registered',
      data: authResponse
    });
  });

  it('should send OTP', () => {
    service.sendOtp({ mobileNumber: '+919876543210' }).subscribe(result => {
      expect(result.success).toBe(true);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/otp/send`);
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, message: 'OTP sent' });
  });

  it('should verify OTP and save session', () => {
    service.verifyOtp({
      mobileNumber: '+919876543210',
      otp: '123456'
    }).subscribe(result => {
      expect(result.token).toBe('test-token');
      expect(service.isLoggedIn()).toBe(true);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/otp/verify`);
    expect(req.request.method).toBe('POST');
    req.flush({
      success: true,
      message: 'OTP verified',
      data: authResponse
    });
  });

  it('should logout and clear session', () => {
    localStorage.setItem('auth_token', 'abc');
    localStorage.setItem('auth_user', JSON.stringify(authResponse));

    service.logout();

    expect(service.getToken()).toBeNull();
    expect(service.getCurrentUser()).toBeNull();
    expect(service.isLoggedIn()).toBe(false)
  });
});