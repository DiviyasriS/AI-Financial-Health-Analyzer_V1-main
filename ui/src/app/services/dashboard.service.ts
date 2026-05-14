import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CategorySummary, MonthlySummary } from './transaction.service';

export interface DashboardSummary {
  totalSpent: number;
  totalTransactions: number;
  averageMonthlySpend: number;
  highestSpendingCategory: string;
  categoryBreakdown: CategorySummary[];
  monthlyBreakdown: MonthlySummary[];
}

export interface RiskData {
  riskLevel: string;      // "Low" | "Medium" | "High" | "Unknown"
  riskScore: number;
  predictedAt: string;
  description: string;
}

export interface InsightData {
  id: number;
  title: string;
  message: string;
  priority: number;
  type: string;           // "info" | "warning" | "danger"
  generatedAt: string;
}
export interface ChartSlice {
  label: string;
  value: number;
  percentage: number;
}

export interface MonthlyBar {
  label: string;
  total: number;
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
  return summary.categoryBreakdown.map((item: CategorySummary): ChartSlice => {
    return {
      label: item.category,
      value: item.total,
      percentage: item.percentageOfTotal
    };
  });
}

toMonthlyBars(summary: DashboardSummary): MonthlyBar[] {
  return summary.monthlyBreakdown.map((item: MonthlySummary): MonthlyBar => {
    return {
      label: item.monthName,
      total: item.total
    };
  });
}
}