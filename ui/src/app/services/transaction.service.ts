import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

// Matches TransactionDto from backend
export interface Transaction {
  id: number;
  date: string;
  description: string;
  amount: number;
  category: string;
}

// Matches CategorySummaryDto from backend
export interface CategorySummary {
  category: string;
  total: number;
  transactionCount: number;
  percentageOfTotal: number;
  topTransactions: Transaction[];
}

// Matches MonthlySummaryDto from backend
export interface MonthlySummary {
  year: number;
  month: number;
  monthName: string;
  total: number;
  transactionCount: number;
  changeFromPreviousMonth: number | null;
  percentageChangeFromPreviousMonth: number | null;
}

// Matches SpendingSummaryDto from backend
export interface SpendingSummary {
  totalSpent: number;
  totalTransactions: number;
  averageTransactionAmount: number;
  averageMonthlySpend: number;
  highestSpendingCategory: string;
  biggestTransaction: Transaction;
  categoryBreakdown: CategorySummary[];
  monthlyBreakdown: MonthlySummary[];
}

// Matches FileProcessingResultDto from backend
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

  constructor(
    private http: HttpClient,
    private authService: AuthService
  ) {}

  // ── Upload file ───────────────────────────────────────────────────────────

  uploadFile(file: File): Observable<UploadResult> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<UploadResult>(
      `${this.apiUrl}/upload`,
      formData,
      { headers: this.getAuthHeaders() }
    );
  }

  // ── Get all transactions ──────────────────────────────────────────────────

  getTransactions(): Observable<Transaction[]> {
    return this.http.get<Transaction[]>(
      this.apiUrl,
      { headers: this.getAuthHeaders() }
    );
  }

  // ── Get spending summary ──────────────────────────────────────────────────

  getSummary(): Observable<SpendingSummary> {
    return this.http.get<SpendingSummary>(
      `${this.apiUrl}/summary`,
      { headers: this.getAuthHeaders() }
    );
  }

  // ── Helper: build auth headers ────────────────────────────────────────────

  private getAuthHeaders(): HttpHeaders {
    const token = this.authService.getToken();
    return new HttpHeaders({
      'Authorization': `Bearer ${token}`
    });
  }
}