import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TransactionService } from './transaction.service';
import { environment } from '../../environments/environment';

describe('TransactionService', () => {
  let service: TransactionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TransactionService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(TransactionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should upload a file', () => {
    const file = new File(['Date,Description,Amount'], 'test.csv', {
      type: 'text/csv'
    });

    service.uploadFile(file).subscribe(result => {
      expect(result.savedCount).toBe(2);
      expect(result.fileType).toBe('CSV');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/transaction/upload`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);

    req.flush({
      savedCount: 2,
      duplicateCount: 0,
      skippedCount: 0,
      totalRowsFound: 2,
      message: 'Uploaded',
      fileType: 'CSV',
      monthWarning: null
    });
  });

  it('should get transactions', () => {
    service.getTransactions().subscribe(result => {
      expect(result.length).toBe(1);
      expect(result[0].category).toBe('Food');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/transaction`);
    expect(req.request.method).toBe('GET');

    req.flush([
      {
        id: 1,
        date: '2026-04-02',
        description: 'Lunch',
        amount: 250,
        category: 'Food'
      }
    ]);
  });

  it('should get transaction summary', () => {
    service.getSummary().subscribe(result => {
      expect(result.totalSpent).toBe(1000);
      expect(result.highestSpendingCategory).toBe('Food');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/transaction/summary`);
    expect(req.request.method).toBe('GET');

    req.flush({
      totalSpent: 1000,
      totalTransactions: 4,
      averageTransactionAmount: 250,
      averageMonthlySpend: 1000,
      highestSpendingCategory: 'Food',
      biggestTransaction: null,
      categoryBreakdown: [],
      monthlyBreakdown: []
    });
  });
});