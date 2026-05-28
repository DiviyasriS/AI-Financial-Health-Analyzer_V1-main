import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, UserProfileDto } from '../../services/auth.service';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.css'
})
export class ProfileComponent implements OnInit {
  profile: UserProfileDto | null = null;

  email = '';
  mobileNumber = '';

  currentPassword = '';
  newPassword = '';
  confirmPassword = '';

  loading = false;
  savingProfile = false;
  changingPassword = false;

  error = '';
  success = '';

  constructor(private authService: AuthService) {}

  ngOnInit(): void {
    this.loadProfile();
  }

  loadProfile(): void {
    this.loading = true;
    this.error = '';
    this.success = '';

    this.authService.getProfile().subscribe({
      next: profile => {
        this.profile = profile;
        this.email = profile.email;
        this.mobileNumber = profile.mobileNumber ?? '';
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load profile.';
        this.loading = false;
      }
    });
  }

  updateProfile(): void {
    this.error = '';
    this.success = '';

    if (!this.email.trim()) {
      this.error = 'Email is required.';
      return;
    }

    this.savingProfile = true;

    this.authService.updateProfile({
      email: this.email.trim(),
      mobileNumber: this.mobileNumber.trim() || null
    }).subscribe({
      next: profile => {
        this.profile = profile;
        this.success = 'Profile updated successfully.';
        this.savingProfile = false;
      },
      error: err => {
        this.error = err?.error?.message || 'Unable to update profile.';
        this.savingProfile = false;
      }
    });
  }

  changePassword(): void {
    this.error = '';
    this.success = '';

    if (!this.currentPassword || !this.newPassword || !this.confirmPassword) {
      this.error = 'Please fill all password fields.';
      return;
    }

    if (this.newPassword.length < 6) {
      this.error = 'New password must be at least 6 characters.';
      return;
    }

    if (this.newPassword !== this.confirmPassword) {
      this.error = 'New password and confirm password do not match.';
      return;
    }

    this.changingPassword = true;

    this.authService.changePassword({
      currentPassword: this.currentPassword,
      newPassword: this.newPassword
    }).subscribe({
      next: () => {
        this.success = 'Password changed successfully.';
        this.currentPassword = '';
        this.newPassword = '';
        this.confirmPassword = '';
        this.changingPassword = false;
      },
      error: err => {
        this.error = err?.error?.message || 'Unable to change password.';
        this.changingPassword = false;
      }
    });
  }
}