import type { PriceConfidence } from '../../api/types';

const STYLES = {
  High: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  Medium: 'bg-amber-50 text-amber-800 ring-amber-200',
  Low: 'bg-red-50 text-red-800 ring-red-200',
} as const;

/** How far to trust a card's price, with the reasons on hover and for screen readers. */
export function ConfidenceBadge({ confidence }: { confidence: PriceConfidence }) {
  const why = confidence.reasons.join(' ');
  return (
    <span
      title={why}
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold ring-1 ring-inset ${STYLES[confidence.level]}`}
    >
      {confidence.level} confidence
      {why && <span className="sr-only">: {why}</span>}
    </span>
  );
}
