import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface Transaction {
  id: number;
  date: string;
  description: string;
  amount: number;
  category: string;
}

export interface CategorySummary {
  category: string;
  total: number;
  transactionCount: number;
  percentageOfTotal: number;
  topTransactions: Transaction[];
}

export interface MonthlySummary {
  year: number;
  month: number;
  monthName: string;
  total: number;
  transactionCount: number;
  changeFromPreviousMonth: number | null;
  percentageChangeFromPreviousMonth: number | null;
}

export interface SpendingSummary {
  totalSpent: number;
  totalTransactions: number;
  averageTransactionAmount: number;
  averageMonthlySpend: number;
  highestSpendingCategory: string;
  biggestTransaction: Transaction | null;
  categoryBreakdown: CategorySummary[];
  monthlyBreakdown: MonthlySummary[];
}

export interface UploadResult {
  savedCount: number;
  duplicateCount: number;
  skippedCount: number;
  totalRowsFound: number;
  message: string;
  fileType: string;
  monthWarning: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class TransactionService {

  private readonly apiUrl = `${environment.apiUrl}/transaction`;

  // HttpClient is injected — interceptor handles auth headers automatically
  constructor(private http: HttpClient) {}

uploadFile(file: File): Observable<UploadResult> {
  const formData = new FormData();
  formData.append('file', file);
  
  return this.http.post<UploadResult>(
    `${this.apiUrl}/upload`,
    formData,
    { responseType: 'json', observe: 'body' }
  );
}

  getTransactions(): Observable<Transaction[]> {
    return this.http.get<Transaction[]>(this.apiUrl);
  }

  getSummary(): Observable<SpendingSummary> {
    return this.http.get<SpendingSummary>(`${this.apiUrl}/summary`);
  }
}