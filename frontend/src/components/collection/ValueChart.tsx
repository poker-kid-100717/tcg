import { CategoryScale, Chart as ChartJS, Filler, LinearScale, LineElement, PointElement, Tooltip } from 'chart.js';
import { Line } from 'react-chartjs-2';

import type { ValuePoint } from '../../api/types';
import { formatDate, formatPrice } from '../../lib/format';

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Filler);

/** What today's collection was worth on each recorded day (market prices, today's quantities). */
export function ValueChart({ history }: { history: ValuePoint[] }) {
  if (history.length < 2) {
    return (
      <p className="rounded-lg bg-slate-50 p-4 text-sm text-slate-600">
        The value chart fills in as prices are recorded, one day at a time.
      </p>
    );
  }
  const first = history[0].value;
  const last = history[history.length - 1].value;
  const color = last >= first ? '#047857' : '#b91c1c';
  return (
    <div
      className="h-56"
      role="img"
      aria-label={`Collection value from ${formatPrice(first)} on ${formatDate(history[0].date, 'short')} to ${formatPrice(last)} on ${formatDate(history[history.length - 1].date, 'short')}`}
    >
      <Line
        data={{
          labels: history.map((p) => formatDate(p.date, 'short')),
          datasets: [
            {
              label: 'Collection value',
              data: history.map((p) => p.value),
              borderColor: color,
              backgroundColor: `${color}14`,
              fill: true,
              tension: 0.25,
              pointRadius: 0,
              pointHoverRadius: 4,
              borderWidth: 2,
            },
          ],
        }}
        options={{
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: 'index', intersect: false },
          plugins: { legend: { display: false }, tooltip: { callbacks: { label: (ctx) => formatPrice(ctx.parsed.y) } } },
          scales: {
            y: { ticks: { callback: (value) => formatPrice(Number(value)), maxTicksLimit: 5 }, grid: { color: '#e2e8f0' } },
            x: { grid: { display: false }, ticks: { maxTicksLimit: 6 } },
          },
        }}
      />
    </div>
  );
}
