import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './register.component.html'
})
export class RegisterComponent {
  email = '';
  password = '';
  mobileNumber = '';
  error = '';
  loading = false;

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  onSubmit(): void {
    if (!this.email || !this.password) {
      this.error = 'Please fill in email and password.';
      return;
    }

    if (this.password.length < 6) {
      this.error = 'Password must be at least 6 characters.';
      return;
    }

    this.loading = true;
    this.error = '';

    this.authService.register({
      email: this.email,
      password: this.password,
      mobileNumber: this.mobileNumber || undefined
    }).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/upload']);
      },
      error: (err) => {
        this.loading = false;
        this.error = err.status === 409
          ? 'An account with this email or mobile number already exists.'
          : 'Registration failed. Please try again.';
      }
    });
  }
}
