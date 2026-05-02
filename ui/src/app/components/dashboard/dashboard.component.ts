import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import {
  TransactionService,
  SpendingSummary
} from '../../services/transaction.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit {

  summary: SpendingSummary | null = null;
  loading = true;
  error   = '';

  constructor(
    private transactionService: TransactionService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadSummary();
  }

  loadSummary(): void {
    this.loading = true;
    this.error   = '';

    this.transactionService.getSummary()
      .subscribe({
        next: (data) => {
          this.summary = data;
          this.loading = false;
        },
        error: () => {
          this.error   = 'Failed to load summary. Please try again.';
          this.loading = false;
        }
      });
  }

  goToUpload(): void {
    this.router.navigate(['/upload']);
  }

  // Helper: show + or - sign on MoM change
  formatChange(value: number | null): string {
    if (value === null) return '—';
    return value >= 0 ? `+₹${value.toFixed(2)}` : `-₹${Math.abs(value).toFixed(2)}`;
  }

  formatChangePct(value: number | null): string {
    if (value === null) return '';
    return value >= 0 ? `+${value.toFixed(1)}%` : `${value.toFixed(1)}%`;
  }

  isPositiveChange(value: number | null): boolean {
    return value !== null && value > 0;
  }
}