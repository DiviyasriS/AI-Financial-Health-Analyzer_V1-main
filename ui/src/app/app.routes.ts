import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { UploadComponent } from './components/upload/upload.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { authGuard } from './guards/auth-guard';
import { ProfileComponent } from './components/profile/profile.component';

export const routes: Routes = [
  // Default route — redirect to login
  { path: '', redirectTo: 'login', pathMatch: 'full' },

  // Public routes — no guard needed
  { path: 'login',    component: LoginComponent },
  { path: 'register', component: RegisterComponent },

  // Protected routes — require valid JWT
  { path: 'upload',    component: UploadComponent,    canActivate: [authGuard] },
  { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'profile', component: ProfileComponent, canActivate: [authGuard] },

  // Catch-all — redirect unknown routes to login
  { path: '**', redirectTo: 'login' }


  
];