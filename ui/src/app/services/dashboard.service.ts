import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CategorySummary, MonthlySummary, Transaction } from './transaction.service';

export interface DashboardSummary {
  totalSpent: number;
  totalReceived: number;
  totalTransactionVolume: number;
  totalTransactions: number;
  averageExpenseAmount: number;
  averageMonthlySpend: number;
  highestSpendingCategory: string;
  biggestTransaction: Transaction | null;
  categoryBreakdown: CategorySummary[];
  monthlyBreakdown: MonthlySummary[];
}

export interface RiskData {
  riskLevel: string;
  riskScore: number;
  predictedAt: string;
  description: string;
}

export interface InsightData {
  id: number;
  title: string;
  message: string;
  priority: number;
  type: string;
  generatedAt: string;
}

export interface ChartSlice {
  label: string;
  value: number;
  percentage: number;
  transactionCount: number;
}

export interface MonthlyBar {
  label: string;
  total: number;
  transactionCount: number;
  year: number;
  month: number;
}

@Injectable({
  providedIn: 'root'
})
export class DashboardService {

  private readonly apiUrl = `${environment.apiUrl}/dashboard`;

  constructor(private http: HttpClient) {}

  getSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.apiUrl}/summary`);
  }

  getRisk(): Observable<RiskData> {
    return this.http.get<RiskData>(`${this.apiUrl}/risk`);
  }

  getInsights(): Observable<InsightData[]> {
    return this.http.get<InsightData[]>(`${this.apiUrl}/insights`);
  }

  toCategorySlices(summary: DashboardSummary): ChartSlice[] {
    return summary.categoryBreakdown
      .filter(item => item.total > 0)
      .map((item: CategorySummary): ChartSlice => ({
        label: item.category,
        value: item.total,
        percentage: item.percentageOfTotal,
        transactionCount: item.transactionCount
      }));
  }

  toMonthlyBars(summary: DashboardSummary): MonthlyBar[] {
    return [...summary.monthlyBreakdown]
      .sort((a, b) => (a.year - b.year) || (a.month - b.month))
      .map((item: MonthlySummary): MonthlyBar => ({
        label: item.monthName,
        total: item.total,
        transactionCount: item.transactionCount,
        year: item.year,
        month: item.month
      }));
  }

  downloadFinancialReport(): Observable<Blob> {
    return this.http.get(`${environment.apiUrl}/reports/financial/pdf`, {
      responseType: 'blob'
    });
  }
}
