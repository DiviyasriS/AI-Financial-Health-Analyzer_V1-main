import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './login.component.html'
})
export class LoginComponent {

  email    = '';
  password = '';
  error    = '';
  loading  = false;

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  onSubmit(): void {
    // Basic validation
    if (!this.email || !this.password) {
      this.error = 'Please enter your email and password.';
      return;
    }

    this.loading = true;
    this.error   = '';

    this.authService.login({ email: this.email, password: this.password })
      .subscribe({
        next: () => {
          this.loading = false;
          // Redirect to dashboard after successful login
          this.router.navigate(['/dashboard']);
        },
        error: (err) => {
          this.loading = false;
          this.error = err.status === 401
            ? 'Invalid email or password.'
            : 'Something went wrong. Please try again.';
        }
      });
  }
}