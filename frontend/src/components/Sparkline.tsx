import type { TrendPoint } from '../api/types';
import { formatPrice } from '../lib/format';

/** A small line of daily market prices, scaled to its own low and high. */
export function Sparkline({ points, className = '' }: { points: TrendPoint[]; className?: string }) {
  if (points.length < 2) return null;
  const width = 96;
  const height = 32;
  const prices = points.map((p) => p.market);
  const min = Math.min(...prices);
  const range = Math.max(...prices) - min || 1;
  const coords = points
    .map((p, i) => {
      const x = (i / (points.length - 1)) * width;
      const y = height - 2 - ((p.market - min) / range) * (height - 4);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(' ');

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      role="img"
      aria-label={`${formatPrice(prices[0])} to ${formatPrice(prices[prices.length - 1])} over ${points.length} days`}
      className={className}
    >
      <polyline points={coords} fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}
