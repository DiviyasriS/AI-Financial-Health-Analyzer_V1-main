import {
  Component,
  OnInit,
  OnDestroy,
  AfterViewInit,
  ViewChild,
  ElementRef,
  ChangeDetectorRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Chart } from 'chart.js';

import {
  DashboardService,
  DashboardSummary,
  RiskData,
  InsightData,
  ChartSlice,
  MonthlyBar,
} from '../../services/dashboard.service';
import { ChartService } from '../../services/chart.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardComponent implements OnInit, AfterViewInit, OnDestroy {

  @ViewChild('categoryChart') categoryRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('monthlyChart') monthlyRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('trendChart') trendRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('topCatChart') topCatRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('riskGaugeChart') riskGaugeRef!: ElementRef<HTMLCanvasElement>;

  summary: DashboardSummary | null = null;
  risk: RiskData | null = null;
  insights: InsightData[] = [];
  categorySlices: ChartSlice[] = [];
  monthlyBars: MonthlyBar[] = [];

  loading = true;
  error = '';
  downloadingReport = false;

  private chartCategory: Chart | null = null;
  private chartMonthly: Chart | null = null;
  private chartTrend: Chart | null = null;
  private chartTopCat: Chart | null = null;
  private chartRiskGauge: Chart | null = null;

  private viewReady = false;
  private dataReady = false;

  constructor(
    private dashboardService: DashboardService,
    private chartService: ChartService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadDashboard();
  }

  ngAfterViewInit(): void {
    this.viewReady = true;
    if (this.dataReady) {
      this.renderAllCharts();
    }
  }

  ngOnDestroy(): void {
    this.destroyCharts();
  }

  loadDashboard(): void {
  this.loading = true;
  this.error = '';
  this.dataReady = false;
  this.destroyCharts();

  forkJoin({
    summary: this.dashboardService.getSummary(),
    risk: this.dashboardService.getRisk().pipe(
      catchError(() =>
        of({
          riskLevel: 'Unknown',
          riskScore: 0,
          predictedAt: new Date().toISOString(),
          description: 'Risk score is temporarily unavailable.',
          riskFactors: [],
          positiveSignals: [],
        })
      )
    ),
    insights: this.dashboardService.getInsights().pipe(
      catchError(() => of([]))
    ),
  }).subscribe({
    next: ({ summary, risk, insights }) => {
      this.summary = summary;
      this.risk = risk;
      this.insights = [...insights].sort((a, b) => b.priority - a.priority);
      this.categorySlices = this.dashboardService.toCategorySlices(summary);
      this.monthlyBars = this.dashboardService.toMonthlyBars(summary);

      this.loading = false;
      this.dataReady = true;
      this.cdr.markForCheck();

      if (this.viewReady) {
        setTimeout(() => this.renderAllCharts(), 0);
      }
    },
    error: () => {
      this.error = 'Failed to load dashboard summary. Please try again.';
      this.loading = false;
      this.cdr.markForCheck();
    },
  });
}

  private destroyCharts(): void {
    this.chartService.destroy(this.chartCategory);
    this.chartService.destroy(this.chartMonthly);
    this.chartService.destroy(this.chartTrend);
    this.chartService.destroy(this.chartTopCat);
    this.chartService.destroy(this.chartRiskGauge);

    this.chartCategory = null;
    this.chartMonthly = null;
    this.chartTrend = null;
    this.chartTopCat = null;
    this.chartRiskGauge = null;
  }

  private renderAllCharts(): void {
    if (!this.summary) return;

    this.renderCategoryDonut();
    this.renderMonthlyBar();
    this.renderTrendLine();
    this.renderTopCategoriesBar();
    if (this.risk) this.renderRiskGauge();
  }

  private renderCategoryDonut(): void {
    if (!this.categoryRef || !this.categorySlices.length) return;

    this.chartCategory = this.chartService.createDoughnut(
      this.categoryRef.nativeElement,
      {
        labels: this.categorySlices.map(s => s.label),
        values: this.categorySlices.map(s => s.value),
      },
      this.chartCategory,
    );
  }

  private renderMonthlyBar(): void {
    if (!this.monthlyRef || !this.monthlyBars.length) return;

    this.chartMonthly = this.chartService.createBar(
      this.monthlyRef.nativeElement,
      {
        labels: this.monthlyBars.map(b => b.label),
        values: this.monthlyBars.map(b => b.total),
      },
      this.chartMonthly,
    );
  }

  private renderTrendLine(): void {
    if (!this.trendRef || this.monthlyBars.length < 2) return;

    this.chartTrend = this.chartService.createLine(
      this.trendRef.nativeElement,
      {
        labels: this.monthlyBars.map(b => b.label),
        values: this.monthlyBars.map(b => b.total),
      },
      this.chartTrend,
    );
  }

  private renderTopCategoriesBar(): void {
    if (!this.topCatRef || !this.categorySlices.length) return;

    const top5 = [...this.categorySlices]
      .sort((a, b) => b.value - a.value)
      .slice(0, 5);

    this.chartTopCat = this.chartService.createHorizontalBar(
      this.topCatRef.nativeElement,
      {
        labels: top5.map(c => c.label),
        values: top5.map(c => c.value),
      },
      this.chartTopCat,
    );
  }

  private renderRiskGauge(): void {
    if (!this.riskGaugeRef || !this.risk) return;

    this.chartRiskGauge = this.chartService.createRiskGauge(
      this.riskGaugeRef.nativeElement,
      {
        score: this.normalizedRiskScore,
        riskLevel: this.risk.riskLevel,
      },
      this.chartRiskGauge,
    );
  }

  get hasData(): boolean {
    return !this.loading && !this.error && (this.summary?.totalTransactions ?? 0) > 0;
  }

  get hasNoData(): boolean {
    return !this.loading && !this.error && (this.summary?.totalTransactions ?? 0) === 0;
  }

  get hasCategoryData(): boolean {
    return this.categorySlices.length > 0;
  }

  get showTrendChart(): boolean {
    return this.monthlyBars.length >= 2;
  }

  get normalizedRiskScore(): number {
  const score = Number(this.risk?.riskScore ?? 0);
  if (!Number.isFinite(score)) return 0;

  const normalized = score > 1 ? score / 100 : score;

  return Math.max(0, Math.min(1, normalized));
}

  get riskPercentage(): number {
    return Math.round(this.normalizedRiskScore * 100);
  }

  get topCategoryText(): string {
    const value = this.summary?.highestSpendingCategory?.trim();
    return value && value !== 'N/A' ? value : 'No spending category';
  }

  get biggestExpenseText(): string {
    const tx = this.summary?.biggestTransaction;
    return tx ? `${tx.description} · ${this.formatCurrency(tx.amount)}` : 'No debit expense found';
  }

  getRiskClass(): string {
    const map: Record<string, string> = {
      Low: 'risk-low',
      Medium: 'risk-medium',
      High: 'risk-danger',
      Unknown: 'risk-unknown',
    };
    return map[this.risk?.riskLevel ?? ''] ?? 'risk-unknown';
  }

  getRiskIcon(): string {
    const map: Record<string, string> = {
      Low: '✅',
      Medium: '⚠️',
      High: '🚨',
      Unknown: '❓',
    };
    return map[this.risk?.riskLevel ?? ''] ?? '❓';
  }

  getRiskColour(): string {
    return this.chartService.riskColour(this.risk?.riskLevel ?? 'Unknown');
  }

  getInsightIcon(type: string): string {
    const map: Record<string, string> = {
      danger: '🚨',
      warning: '⚠️',
      info: 'ℹ️',
    };
    return map[type] ?? 'ℹ️';
  }

  formatCurrency(value: number | null | undefined): string {
    const safeValue = Number(value ?? 0);
    return safeValue.toLocaleString('en-IN', {
      style: 'currency',
      currency: 'INR',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  }

  formatPercentage(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${Number(value).toFixed(1)}%`;
  }

  formatChange(value: number | null): string {
    if (value === null) return '—';
    const prefix = value > 0 ? '+' : value < 0 ? '-' : '';
    return `${prefix}${this.formatCurrency(Math.abs(value))}`;
  }

  formatChangePct(value: number | null): string {
    if (value === null) return '—';
    const prefix = value > 0 ? '+' : '';
    return `${prefix}${value.toFixed(1)}%`;
  }

  isPositiveChange(value: number | null): boolean {
    return value !== null && value > 0;
  }

  downloadReport(): void {
    this.downloadingReport = true;
    this.error = '';
    this.cdr.markForCheck();

    this.dashboardService.downloadFinancialReport().subscribe({
      next: (blob: Blob) => {
        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');

        anchor.href = url;
        anchor.download = `financial-health-report-${new Date().toISOString().slice(0, 10)}.pdf`;
        anchor.click();

        window.URL.revokeObjectURL(url);
        this.downloadingReport = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.error = 'Failed to download PDF report. Please try again.';
        this.downloadingReport = false;
        this.cdr.markForCheck();
      },
    });
  }

  goToUpload(): void {
    this.router.navigate(['/upload']);
  }
}
