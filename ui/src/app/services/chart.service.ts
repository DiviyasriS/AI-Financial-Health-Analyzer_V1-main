/**
 * ChartService
 *
 * Centralises all Chart.js creation / destruction logic.
 * Angular components inject this service, pass a <canvas> element reference
 * and typed data, and get back a managed Chart instance.
 *
 * No `any` — all inputs and outputs are strongly typed.
 */

import { Injectable } from '@angular/core';
import {
  Chart,
  ChartConfiguration,
  ChartType,
  registerables,
} from 'chart.js';

Chart.register(...registerables);

// ── Colour palette used across all charts ────────────────────────────────

const PALETTE = [
  '#6366f1', // indigo-500
  '#8b5cf6', // violet-500
  '#06b6d4', // cyan-500
  '#10b981', // emerald-500
  '#f59e0b', // amber-500
  '#ef4444', // red-500
  '#64748b', // slate-500 (catch-all / "Other")
];

const PALETTE_ALPHA = PALETTE.map(h => h + 'cc'); // 80% opacity for bars

// ── Risk-level colour map ────────────────────────────────────────────────

const RISK_COLOURS: Record<string, string> = {
  Low:     '#10b981',
  Medium:  '#f59e0b',
  High:    '#ef4444',
  Unknown: '#64748b',
};

// ── Typed input shapes ───────────────────────────────────────────────────

export interface DonutInput {
  labels:      string[];
  values:      number[];
  currency?:   string;
}

export interface BarInput {
  labels:      string[];
  values:      number[];
  currency?:   string;
  colour?:     string;
}

export interface GaugeInput {
  score:       number;   // 0–1
  riskLevel:   string;
}

@Injectable({ providedIn: 'root' })
export class ChartService {

  /** Destroy an existing chart before replacing it */
  destroy(chart: Chart | null): void {
    chart?.destroy();
  }

  // ── Doughnut / pie ───────────────────────────────────────────────────

  createDoughnut(
    canvas:   HTMLCanvasElement,
    input:    DonutInput,
    existing: Chart | null
  ): Chart {
    this.destroy(existing);

    const currency = input.currency ?? '₹';

    const config: ChartConfiguration<'doughnut'> = {
      type: 'doughnut',
      data: {
        labels:   input.labels,
        datasets: [{
          data:            input.values,
          backgroundColor: PALETTE,
          borderColor:     '#1e1b4b',
          borderWidth:     2,
          hoverOffset:     8,
        }],
      },
      options: {
        responsive:          true,
        maintainAspectRatio: false,
        cutout:              '68%',
        plugins: {
          legend: {
            position: 'right',
            labels: {
              color:     '#e2e8f0',
              font:      { size: 12, family: "'DM Mono', monospace" },
              padding:   16,
              usePointStyle: true,
              pointStyleWidth: 10,
            },
          },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const val = ctx.parsed as number;
                return ` ${currency}${val.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`;
              },
            },
          },
        },
      },
    };

    return new Chart(canvas, config);
  }

  // ── Vertical bar (monthly trend) ─────────────────────────────────────

  createBar(
    canvas:   HTMLCanvasElement,
    input:    BarInput,
    existing: Chart | null
  ): Chart {
    this.destroy(existing);

    const currency = input.currency ?? '₹';
    const colour   = input.colour   ?? PALETTE[0];

    const config: ChartConfiguration<'bar'> = {
      type: 'bar',
      data: {
        labels:   input.labels,
        datasets: [{
          label:           'Monthly Spend',
          data:            input.values,
          backgroundColor: PALETTE_ALPHA,
          borderColor:     PALETTE,
          borderWidth:     2,
          borderRadius:    6,
          borderSkipped:   false,
        }],
      },
      options: {
        responsive:          true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: {
            ticks: { color: '#94a3b8', font: { size: 11 } },
            grid:  { color: 'rgba(255,255,255,0.05)' },
          },
          y: {
            ticks: {
              color: '#94a3b8',
              font:  { size: 11 },
              callback: (val) =>
                `${currency}${Number(val).toLocaleString('en-IN')}`,
            },
            grid:  { color: 'rgba(255,255,255,0.07)' },
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const val = ctx.parsed.y as number;
                return ` ${currency}${val.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`;
              },
            },
          },
        },
      },
    };

    return new Chart(canvas, config);
  }

  // ── Horizontal bar (top categories) ──────────────────────────────────

  createHorizontalBar(
    canvas:   HTMLCanvasElement,
    input:    BarInput,
    existing: Chart | null
  ): Chart {
    this.destroy(existing);

    const currency = input.currency ?? '₹';

    const config: ChartConfiguration<'bar'> = {
      type: 'bar',
      data: {
        labels:   input.labels,
        datasets: [{
          label:           'Spend',
          data:            input.values,
          backgroundColor: PALETTE_ALPHA,
          borderColor:     PALETTE,
          borderWidth:     2,
          borderRadius:    4,
          borderSkipped:   false,
        }],
      },
      options: {
        indexAxis:           'y',
        responsive:          true,
        maintainAspectRatio: false,
        scales: {
          x: {
            ticks: {
              color: '#94a3b8',
              font:  { size: 11 },
              callback: (val) =>
                `${currency}${Number(val).toLocaleString('en-IN')}`,
            },
            grid: { color: 'rgba(255,255,255,0.05)' },
          },
          y: {
            ticks: { color: '#e2e8f0', font: { size: 12 } },
            grid:  { display: false },
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const val = ctx.parsed.x as number;
                return ` ${currency}${val.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`;
              },
            },
          },
        },
      },
    };

    return new Chart(canvas, config);
  }

  // ── Risk gauge (semi-circle doughnut) ────────────────────────────────

  createRiskGauge(
    canvas:   HTMLCanvasElement,
    input:    GaugeInput,
    existing: Chart | null
  ): Chart {
    this.destroy(existing);

    const colour  = RISK_COLOURS[input.riskLevel] ?? RISK_COLOURS['Unknown'];
    const filled  = input.score;
    const empty   = 1 - filled;

    const config: ChartConfiguration<'doughnut'> = {
      type: 'doughnut',
      data: {
        datasets: [{
          data:            [filled, empty],
          backgroundColor: [colour, 'rgba(255,255,255,0.06)'],
          borderWidth:     0,
          circumference:   180,
          rotation:        270,
        }],
      },
      options: {
        responsive:          true,
        maintainAspectRatio: false,
        cutout:              '78%',
        plugins: {
          legend:  { display: false },
          tooltip: { enabled: false },
        },
      },
    };

    return new Chart(canvas, config);
  }

  // ── Line chart (spending trend) ──────────────────────────────────────

  createLine(
    canvas:   HTMLCanvasElement,
    input:    BarInput,
    existing: Chart | null
  ): Chart {
    this.destroy(existing);

    const currency = input.currency ?? '₹';

    const config: ChartConfiguration<'line'> = {
      type: 'line',
      data: {
        labels:   input.labels,
        datasets: [{
          label:           'Spending',
          data:            input.values,
          borderColor:     PALETTE[0],
          backgroundColor: `${PALETTE[0]}22`,
          pointBackgroundColor: PALETTE[0],
          pointBorderColor:     '#1e1b4b',
          pointBorderWidth:     2,
          pointRadius:          5,
          tension:              0.4,
          fill:                 true,
        }],
      },
      options: {
        responsive:          true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: {
            ticks: { color: '#94a3b8', font: { size: 11 } },
            grid:  { color: 'rgba(255,255,255,0.05)' },
          },
          y: {
            ticks: {
              color: '#94a3b8',
              font:  { size: 11 },
              callback: (val) =>
                `${currency}${Number(val).toLocaleString('en-IN')}`,
            },
            grid: { color: 'rgba(255,255,255,0.07)' },
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const val = ctx.parsed.y as number;
                return ` ${currency}${val.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`;
              },
            },
          },
        },
      },
    };

    return new Chart(canvas, config);
  }

  /** Returns the hex colour string for a given risk level */
  riskColour(level: string): string {
    return RISK_COLOURS[level] ?? RISK_COLOURS['Unknown'];
  }
}
