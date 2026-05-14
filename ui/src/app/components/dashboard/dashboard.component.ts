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
import { forkJoin } from 'rxjs';
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

  // ── Template refs for chart canvases ─────────────────────────────────
  @ViewChild('categoryChart')   categoryRef!:   ElementRef<HTMLCanvasElement>;
  @ViewChild('monthlyChart')    monthlyRef!:    ElementRef<HTMLCanvasElement>;
  @ViewChild('trendChart')      trendRef!:      ElementRef<HTMLCanvasElement>;
  @ViewChild('topCatChart')     topCatRef!:     ElementRef<HTMLCanvasElement>;
  @ViewChild('riskGaugeChart')  riskGaugeRef!:  ElementRef<HTMLCanvasElement>;

  // ── State ─────────────────────────────────────────────────────────────
  summary:       DashboardSummary | null = null;
  risk:          RiskData | null         = null;
  insights:      InsightData[]           = [];
  categorySlices: ChartSlice[]           = [];
  monthlyBars:   MonthlyBar[]            = [];

  loading = true;
  error   = '';

  // ── Chart instances ───────────────────────────────────────────────────
  private chartCategory:  Chart | null = null;
  private chartMonthly:   Chart | null = null;
  private chartTrend:     Chart | null = null;
  private chartTopCat:    Chart | null = null;
  private chartRiskGauge: Chart | null = null;

  // Flag to track whether the view is ready for chart rendering
  private viewReady = false;
  private dataReady = false;

  constructor(
    private dashboardService: DashboardService,
    private chartService:     ChartService,
    private router:           Router,
    private cdr:              ChangeDetectorRef,
  ) {}

  // ── Lifecycle ─────────────────────────────────────────────────────────

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
    this.chartService.destroy(this.chartCategory);
    this.chartService.destroy(this.chartMonthly);
    this.chartService.destroy(this.chartTrend);
    this.chartService.destroy(this.chartTopCat);
    this.chartService.destroy(this.chartRiskGauge);
  }

  // ── Data loading ──────────────────────────────────────────────────────

  loadDashboard(): void {
    this.loading   = true;
    this.error     = '';
    this.dataReady = false;

    forkJoin({
      summary:  this.dashboardService.getSummary(),
      risk:     this.dashboardService.getRisk(),
      insights: this.dashboardService.getInsights(),
    }).subscribe({
      next: ({ summary, risk, insights }) => {
        this.summary        = summary;
        this.risk           = risk;
        this.insights       = insights;
        this.categorySlices = this.dashboardService.toCategorySlices(summary);
        this.monthlyBars    = this.dashboardService.toMonthlyBars(summary);
        this.loading        = false;
        this.dataReady      = true;
        this.cdr.markForCheck();

        // AfterViewInit may have already fired — render immediately
        if (this.viewReady) {
          // Give Angular one tick to stamp the canvas elements
          setTimeout(() => this.renderAllCharts(), 0);
        }
      },
      error: () => {
        this.error   = 'Failed to load dashboard data. Please try again.';
        this.loading = false;
        this.cdr.markForCheck();
      },
    });
  }

  // ── Chart rendering ───────────────────────────────────────────────────

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
    if (!this.trendRef || !this.monthlyBars.length) return;

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
    if (!this.topCatRef || !this.summary?.categoryBreakdown.length) return;

    const top5 = [...this.summary.categoryBreakdown]
      .sort((a, b) => b.total - a.total)
      .slice(0, 5);

    this.chartTopCat = this.chartService.createHorizontalBar(
      this.topCatRef.nativeElement,
      {
        labels: top5.map(c => c.category),
        values: top5.map(c => c.total),
      },
      this.chartTopCat,
    );
  }

  private renderRiskGauge(): void {
    if (!this.riskGaugeRef || !this.risk) return;

    this.chartRiskGauge = this.chartService.createRiskGauge(
      this.riskGaugeRef.nativeElement,
      {
        score:     this.risk.riskScore,
        riskLevel: this.risk.riskLevel,
      },
      this.chartRiskGauge,
    );
  }

  // ── Template helpers ──────────────────────────────────────────────────

  get hasData(): boolean {
    return !this.loading && !this.error && (this.summary?.totalTransactions ?? 0) > 0;
  }

  get hasNoData(): boolean {
    return !this.loading && !this.error && (this.summary?.totalTransactions ?? 0) === 0;
  }

  getRiskClass(): string {
    const map: Record<string, string> = {
      Low:     'risk-low',
      Medium:  'risk-medium',
      High:    'risk-danger',
      Unknown: 'risk-unknown',
    };
    return map[this.risk?.riskLevel ?? ''] ?? '';
  }

  getRiskIcon(): string {
    const map: Record<string, string> = {
      Low:     '✅',
      Medium:  '⚠️',
      High:    '🚨',
      Unknown: '❓',
    };
    return map[this.risk?.riskLevel ?? ''] ?? '❓';
  }

  getRiskColour(): string {
    return this.chartService.riskColour(this.risk?.riskLevel ?? '');
  }

  formatCurrency(value: number): string {
    return `₹${value.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`;
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

  getInsightIcon(type: string): string {
    const map: Record<string, string> = {
      danger:  '🚨',
      warning: '⚠️',
      info:    'ℹ️',
    };
    return map[type] ?? 'ℹ️';
  }

  goToUpload(): void {
    this.router.navigate(['/upload']);
  }
}