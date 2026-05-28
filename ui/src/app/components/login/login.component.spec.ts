import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { ChangeDetectorRef } from '@angular/core';
import { LoginComponent } from './login.component';
import { AuthService } from '../../services/auth.service';
import { ActivatedRoute } from '@angular/router';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;

  let loginCalled = false;
  let sendOtpCalled = false;
  let verifyOtpCalled = false;
  let navigateCalledWith: any[] | null = null;

  const authServiceMock = {
    login: (_data: any) => {
      loginCalled = true;
      return of({});
    },
    sendOtp: (_data: any) => {
      sendOtpCalled = true;
      return of({});
    },
    verifyOtp: (_data: any) => {
      verifyOtpCalled = true;
      return of({});
    },
    googleLogin: () => of({})
  };

  const routerMock = {
    navigate: (commands: any[]) => {
      navigateCalledWith = commands;
      return Promise.resolve(true);
    }
  };

  beforeEach(async () => {
    loginCalled = false;
    sendOtpCalled = false;
    verifyOtpCalled = false;
    navigateCalledWith = null;

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
  { provide: AuthService, useValue: authServiceMock },
  { provide: Router, useValue: routerMock },
  {
    provide: ActivatedRoute,
    useValue: {
      snapshot: {
        paramMap: new Map(),
        queryParamMap: new Map()
      }
    }
  },
  ChangeDetectorRef
]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
  });

  it('should show error when email or password is missing', () => {
    component.onSubmit();

    expect(component.error).toBe('Please enter your email and password.');
    expect(loginCalled).toBe(false);
  });

  it('should login and navigate to dashboard', () => {
    authServiceMock.login = (_data: any) => {
      loginCalled = true;
      return of({
        token: 'token',
        email: 'test@example.com',
        userId: 1
      });
    };

    component.email = 'test@example.com';
    component.password = 'Password@123';

    component.onSubmit();

    expect(loginCalled).toBe(true);
    expect(component.loading).toBe(false);
    expect(navigateCalledWith).toEqual(['/dashboard']);
  });

  it('should show invalid login error for 401', () => {
    authServiceMock.login = (_data: any) => {
      loginCalled = true;
      return throwError(() => ({ status: 401 }));
    };

    component.email = 'wrong@example.com';
    component.password = 'wrong';

    component.onSubmit();

    expect(component.loading).toBe(false);
    expect(component.error).toBe('Invalid email or password.');
  });

  it('should show error when mobile number is missing for OTP', () => {
    component.sendOtp();

    expect(component.error).toBe('Please enter your mobile number.');
    expect(sendOtpCalled).toBe(false);
  });

  it('should send OTP successfully', () => {
  authServiceMock.sendOtp = (_data: any) => {
    sendOtpCalled = true;
    return of({
      success: true,
      message: 'OTP sent'
    });
  };

  component.mobileNumber = '+919876543210';
  component.sendOtp();

  expect(sendOtpCalled).toBe(true);
  expect(component.error).toBe('');
});

  it('should verify OTP and navigate to dashboard', () => {
    authServiceMock.verifyOtp = (_data: any) => {
      verifyOtpCalled = true;
      return of({
        token: 'token',
        email: 'test@example.com',
        userId: 1
      });
    };

    component.mobileNumber = '+919876543210';
    component.otp = '123456';

    component.verifyOtp();

    expect(verifyOtpCalled).toBe(true);
    expect(navigateCalledWith).toEqual(['/dashboard']);
  });
});