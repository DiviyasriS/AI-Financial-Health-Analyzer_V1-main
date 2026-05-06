import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { DashboardService, DashboardSummary, RiskData, InsightData } from '../../services/dashboard.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit {

  summary: DashboardSummary | null = null;
  risk: RiskData | null = null;
  insights: InsightData[] = [];

  loading = true;
  error   = '';

  constructor(
    private dashboardService: DashboardService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.error   = '';

    // Load all three endpoints in parallel
    forkJoin({
      summary:  this.dashboardService.getSummary(),
      risk:     this.dashboardService.getRisk(),
      insights: this.dashboardService.getInsights()
    }).subscribe({
      next: ({ summary, risk, insights }) => {
        this.summary  = summary;
        this.risk     = risk;
        this.insights = insights;
        this.loading  = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.error   = 'Failed to load dashboard. Please try again.';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  goToUpload(): void {
    this.router.navigate(['/upload']);
  }

  getRiskClass(): string {
    if (!this.risk) return '';
    return {
      'Low':     'risk-low',
      'Medium':  'risk-medium',
      'High':    'risk-danger',
      'Unknown': 'risk-unknown'
    }[this.risk.riskLevel] ?? '';
  }

  getRiskIcon(): string {
    return {
      'Low':     '✅',
      'Medium':  '⚠️',
      'High':    '🚨',
      'Unknown': '❓'
    }[this.risk?.riskLevel ?? ''] ?? '❓';
  }

  formatChange(value: number | null): string {
    if (value === null) return '—';
    return value >= 0
      ? `+₹${value.toFixed(2)}`
      : `-₹${Math.abs(value).toFixed(2)}`;
  }

  formatChangePct(value: number | null): string {
    if (value === null) return '';
    return value >= 0 ? `+${value.toFixed(1)}%` : `${value.toFixed(1)}%`;
  }

  isPositiveChange(value: number | null): boolean {
    return value !== null && value > 0;
  }
}