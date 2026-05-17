import { AfterViewInit, Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { environment } from '../../../environments/environment';

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: { client_id: string; callback: (response: { credential: string }) => void }) => void;
          renderButton: (element: HTMLElement | null, options: object) => void;
        };
      };
    };
  }
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './login.component.html'
})
export class LoginComponent implements AfterViewInit {
  email = '';
  password = '';
  mobileNumber = '';
  otp = '';
  otpSent = false;
  error = '';
  success = '';
  loading = false;
  otpLoading = false;

constructor(
  private authService: AuthService,
  private router: Router,
  private cdr: ChangeDetectorRef
) {}

  ngAfterViewInit(): void {
    this.renderGoogleButton();
  }

  onSubmit(): void {
    if (!this.email || !this.password) {
      this.error = 'Please enter your email and password.';
      return;
    }

    this.loading = true;
    this.error = '';

    this.authService.login({ email: this.email, password: this.password }).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading = false;
        this.error = err.status === 401 ? 'Invalid email or password.' : 'Something went wrong. Please try again.';
      }
    });
  }
sendOtp(): void {
  if (!this.mobileNumber) {
    this.error = 'Please enter your mobile number.';
    return;
  }

  this.otpLoading = true;
  this.error = '';
  this.success = '';
  this.cdr.detectChanges();

  this.authService.sendOtp({ mobileNumber: this.mobileNumber }).subscribe({
    next: (response) => {
      console.log('OTP success:', response);

      setTimeout(() => {
        this.otpLoading = false;
        this.otpSent = true;
        this.success = 'OTP sent successfully. Check backend terminal for OTP.';
        this.cdr.detectChanges();
      }, 0);
    },
    error: (err) => {
      console.error('OTP failed:', err);

      this.otpLoading = false;
      this.error = err.error?.message || 'Unable to send OTP.';
      this.cdr.detectChanges();
    }
  });
}

  verifyOtp(): void {
    if (!this.mobileNumber || !this.otp) {
      this.error = 'Please enter mobile number and OTP.';
      return;
    }

    this.otpLoading = true;
    this.error = '';

    this.authService.verifyOtp({ mobileNumber: this.mobileNumber, otp: this.otp }).subscribe({
      next: () => {
        this.otpLoading = false;
        this.router.navigate(['/dashboard']);
      },
      error: () => {
        this.otpLoading = false;
        this.error = 'Invalid or expired OTP.';
      }
    });
  }

  private renderGoogleButton(): void {
    if (!environment.googleClientId || !window.google?.accounts?.id) {
      return;
    }

    window.google.accounts.id.initialize({
      client_id: environment.googleClientId,
      callback: (response: { credential: string }) => this.handleGoogleCredential(response.credential)
    });

    window.google.accounts.id.renderButton(document.getElementById('googleSignInButton'), {
      theme: 'outline',
      size: 'large',
      width: 320
    });
  }

  private handleGoogleCredential(credential: string): void {
    this.loading = true;
    this.error = '';

    this.authService.googleLogin(credential).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/dashboard']);
      },
      error: () => {
        this.loading = false;
        this.error = 'Google Sign-In failed.';
      }
    });
  }
}
