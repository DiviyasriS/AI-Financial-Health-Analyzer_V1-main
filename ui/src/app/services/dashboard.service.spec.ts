import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DashboardService, DashboardSummary } from './dashboard.service';
import { environment } from '../../environments/environment';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;

  const summary: DashboardSummary = {
    totalSpent: 1500,
    totalTransactions: 3,
    averageMonthlySpend: 1500,
    highestSpendingCategory: 'Food',
    categoryBreakdown: [
      {
        category: 'Food',
        total: 1000,
        transactionCount: 2,
        percentageOfTotal: 66.6,
        topTransactions: []
      }
    ],
    monthlyBreakdown: [
      {
        year: 2026,
        month: 4,
        monthName: 'April',
        total: 1500,
        transactionCount: 3,
        changeFromPreviousMonth: null,
        percentageChangeFromPreviousMonth: null
      }
    ]
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DashboardService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should get dashboard summary', () => {
    service.getSummary().subscribe(result => {
      expect(result.totalSpent).toBe(1500);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/dashboard/summary`);
    expect(req.request.method).toBe('GET');
    req.flush(summary);
  });

  it('should get risk data', () => {
    service.getRisk().subscribe(result => {
      expect(result.riskLevel).toBe('Low');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/dashboard/risk`);
    expect(req.request.method).toBe('GET');

    req.flush({
      riskLevel: 'Low',
      riskScore: 20,
      predictedAt: '2026-05-27',
      description: 'Healthy'
    });
  });

  it('should get insights', () => {
    service.getInsights().subscribe(result => {
      expect(result.length).toBe(1);
      expect(result[0].type).toBe('info');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/dashboard/insights`);
    expect(req.request.method).toBe('GET');

    req.flush([
      {
        id: 1,
        title: 'Good savings',
        message: 'You are doing well',
        priority: 1,
        type: 'info',
        generatedAt: '2026-05-27'
      }
    ]);
  });

  it('should convert category breakdown to chart slices', () => {
    const result = service.toCategorySlices(summary);

    expect(result.length).toBe(1);
    expect(result[0].label).toBe('Food');
    expect(result[0].value).toBe(1000);
  });

  it('should convert monthly breakdown to monthly bars', () => {
    const result = service.toMonthlyBars(summary);

    expect(result.length).toBe(1);
    expect(result[0].label).toBe('April');
    expect(result[0].total).toBe(1500);
  });

  it('should download financial report', () => {
    service.downloadFinancialReport().subscribe(blob => {
      expect(blob instanceof Blob).toBe(true);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/reports/financial/pdf`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');

    req.flush(new Blob(['pdf-content'], { type: 'application/pdf' }));
  });
});