import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DashboardService, DashboardSummary, RiskData, InsightData } from './dashboard.service';
import { environment } from '../../environments/environment';

const API = `${environment.apiUrl}/dashboard`;

const makeSummary = (overrides?: Partial<DashboardSummary>): DashboardSummary => ({
  totalSpent: 5000,
  totalReceived: 30000,
  totalTransactionVolume: 35000,
  totalTransactions: 10,
  averageExpenseAmount: 500,
  averageMonthlySpend: 5000,
  highestSpendingCategory: 'Food',
  biggestTransaction: null,
  categoryBreakdown: [
    { category: 'Food', total: 3000, transactionCount: 6, percentageOfTotal: 60, topTransactions: [] },
    { category: 'Shopping', total: 2000, transactionCount: 4, percentageOfTotal: 40, topTransactions: [] }
  ],
  monthlyBreakdown: [
    { year: 2026, month: 5, monthName: 'May 2026', total: 3000, transactionCount: 6, changeFromPreviousMonth: 1000, percentageChangeFromPreviousMonth: 50 },
    { year: 2026, month: 4, monthName: 'April 2026', total: 2000, transactionCount: 4, changeFromPreviousMonth: null, percentageChangeFromPreviousMonth: null }
  ],
  ...overrides
});

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DashboardService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  // ─── getSummary ────────────────────────────────────────────────────────────

  it('getSummary() GETs /dashboard/summary', () => {
    service.getSummary().subscribe(res => {
      expect(res.totalSpent).toBe(5000);
      expect(res.highestSpendingCategory).toBe('Food');
    });

    const req = httpMock.expectOne(`${API}/summary`);
    expect(req.request.method).toBe('GET');
    req.flush(makeSummary());
  });

  it('getSummary() passes through category and monthly breakdown', () => {
    service.getSummary().subscribe(res => {
      expect(res.categoryBreakdown).toHaveLength(2);
      expect(res.monthlyBreakdown).toHaveLength(2);
    });

    httpMock.expectOne(`${API}/summary`).flush(makeSummary());
  });

  // ─── getRisk ───────────────────────────────────────────────────────────────

  it('getRisk() GETs /dashboard/risk and returns risk data', () => {
    const riskData: RiskData = {
      riskLevel: 'Low',
      riskScore: 0.2,
      predictedAt: '2026-05-01',
      description: 'Healthy',
      riskFactors: [],
      positiveSignals: ['Stable spending']
    };

    service.getRisk().subscribe(res => {
      expect(res.riskLevel).toBe('Low');
      expect(res.riskScore).toBe(0.2);
      expect(res.positiveSignals).toContain('Stable spending');
    });

    httpMock.expectOne(`${API}/risk`).flush(riskData);
  });

  it('getRisk() works for High risk level', () => {
    service.getRisk().subscribe(res => {
      expect(res.riskLevel).toBe('High');
      expect(res.riskFactors.length).toBeGreaterThan(0);
    });

    httpMock.expectOne(`${API}/risk`).flush({
      riskLevel: 'High',
      riskScore: 0.85,
      predictedAt: '2026-05-01',
      description: 'At risk',
      riskFactors: ['High food spend', 'Volatile spending'],
      positiveSignals: []
    });
  });

  // ─── getInsights ───────────────────────────────────────────────────────────

  it('getInsights() GETs /dashboard/insights and returns list', () => {
    const insights: InsightData[] = [
      { id: 1, title: 'Good savings', message: 'Well done', priority: 1, type: 'info', generatedAt: '2026-05-01' },
      { id: 2, title: 'High food spend', message: 'Watch it', priority: 2, type: 'warning', generatedAt: '2026-05-01' }
    ];

    service.getInsights().subscribe(res => {
      expect(res).toHaveLength(2);
      expect(res[0].type).toBe('info');
      expect(res[1].type).toBe('warning');
    });

    httpMock.expectOne(`${API}/insights`).flush(insights);
  });

  it('getInsights() returns empty list when no insights', () => {
    service.getInsights().subscribe(res => {
      expect(res).toHaveLength(0);
    });
    httpMock.expectOne(`${API}/insights`).flush([]);
  });

  // ─── toCategorySlices ──────────────────────────────────────────────────────

  it('toCategorySlices() maps category breakdown to chart slices', () => {
    const slices = service.toCategorySlices(makeSummary());

    expect(slices).toHaveLength(2);
    expect(slices[0].label).toBe('Food');
    expect(slices[0].value).toBe(3000);
    expect(slices[0].percentage).toBe(60);
    expect(slices[0].transactionCount).toBe(6);
  });

  it('toCategorySlices() filters out zero-value categories', () => {
    const summary = makeSummary({
      categoryBreakdown: [
        { category: 'Food', total: 1000, transactionCount: 3, percentageOfTotal: 100, topTransactions: [] },
        { category: 'Empty', total: 0, transactionCount: 0, percentageOfTotal: 0, topTransactions: [] }
      ]
    });

    const slices = service.toCategorySlices(summary);

    expect(slices).toHaveLength(1);
    expect(slices[0].label).toBe('Food');
  });

  it('toCategorySlices() returns empty array when breakdown is empty', () => {
    const slices = service.toCategorySlices(makeSummary({ categoryBreakdown: [] }));
    expect(slices).toHaveLength(0);
  });

  // ─── toMonthlyBars ─────────────────────────────────────────────────────────

  it('toMonthlyBars() maps monthly breakdown to bars', () => {
    const bars = service.toMonthlyBars(makeSummary());

    expect(bars).toHaveLength(2);
    expect(bars[0].label).toBe('April 2026');
    expect(bars[0].total).toBe(2000);
    expect(bars[1].label).toBe('May 2026');
    expect(bars[1].total).toBe(3000);
  });

  it('toMonthlyBars() sorts by year then month ascending', () => {
    const summary = makeSummary({
      monthlyBreakdown: [
        { year: 2026, month: 5, monthName: 'May 2026', total: 3000, transactionCount: 5, changeFromPreviousMonth: null, percentageChangeFromPreviousMonth: null },
        { year: 2026, month: 3, monthName: 'March 2026', total: 1000, transactionCount: 3, changeFromPreviousMonth: null, percentageChangeFromPreviousMonth: null },
        { year: 2026, month: 4, monthName: 'April 2026', total: 2000, transactionCount: 4, changeFromPreviousMonth: null, percentageChangeFromPreviousMonth: null }
      ]
    });

    const bars = service.toMonthlyBars(summary);

    expect(bars[0].month).toBe(3); // March first
    expect(bars[1].month).toBe(4); // April second
    expect(bars[2].month).toBe(5); // May third
  });

  it('toMonthlyBars() returns empty array when breakdown is empty', () => {
    const bars = service.toMonthlyBars(makeSummary({ monthlyBreakdown: [] }));
    expect(bars).toHaveLength(0);
  });

  // ─── downloadFinancialReport ───────────────────────────────────────────────

  it('downloadFinancialReport() GETs PDF with responseType blob', () => {
    service.downloadFinancialReport().subscribe(blob => {
      expect(blob instanceof Blob).toBe(true);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/reports/financial/pdf`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));
  });
});