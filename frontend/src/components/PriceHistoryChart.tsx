import {
  CategoryScale,
  Chart as ChartJS,
  Filler,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  Tooltip,
} from 'chart.js';
import { Line } from 'react-chartjs-2';

import type { PricePoint } from '../api/types';
import { formatDate, formatPrice } from '../lib/format';

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend, Filler);

const COLORS = ['#3B4CCA', '#E0A800', '#0F9D58', '#EE1515', '#7B61FF', '#00838F'];

const label = (variant: string) =>
  variant.replace(/(?<=[a-z0-9])(?=[A-Z])/g, ' ').replace(/^./, (c) => c.toUpperCase());

/** Market price over time, one line per printing, from the daily snapshots. */
export function PriceHistoryChart({ history }: { history: PricePoint[] }) {
  const dates = [...new Set(history.map((p) => p.date))].sort();
  const variants = [...new Set(history.map((p) => p.variant))];

  if (dates.length < 2) {
    return (
      <p className="rounded-lg bg-slate-50 p-4 text-sm text-slate-600">
        Price history builds up one snapshot a day. {dates.length === 1 ? 'The first one is in; ' : ''}
        check back tomorrow for a trend line.
      </p>
    );
  }

  const byKey = new Map(history.map((p) => [`${p.variant}|${p.date}`, p.market]));
  const data = {
    labels: dates.map((d) => formatDate(d, 'short')),
    datasets: variants.map((variant, i) => ({
      label: label(variant),
      data: dates.map((d) => byKey.get(`${variant}|${d}`) ?? null),
      borderColor: COLORS[i % COLORS.length],
      backgroundColor: `${COLORS[i % COLORS.length]}1A`,
      fill: variants.length === 1,
      spanGaps: true,
      tension: 0.25,
      pointRadius: dates.length > 60 ? 0 : 2,
      borderWidth: 2,
    })),
  };

  return (
    <div className="h-72" role="img" aria-label={`Market price history across ${dates.length} days`}>
      <Line
        data={data}
        options={{
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: 'index', intersect: false },
          plugins: {
            legend: { display: variants.length > 1, position: 'bottom' },
            tooltip: { callbacks: { label: (ctx) => `${ctx.dataset.label}: ${formatPrice(ctx.parsed.y)}` } },
          },
          scales: {
            y: { ticks: { callback: (value) => formatPrice(Number(value)) }, grid: { color: '#e2e8f0' } },
            x: { grid: { display: false }, ticks: { maxTicksLimit: 8 } },
          },
        }}
      />
    </div>
  );
}
