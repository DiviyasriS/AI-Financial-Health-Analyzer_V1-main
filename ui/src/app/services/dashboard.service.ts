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
}