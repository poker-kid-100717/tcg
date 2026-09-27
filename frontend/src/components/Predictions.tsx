import { Link } from 'react-router-dom';

import type { CardPrediction, PredictionList, PredictionReason, PredictionStatus } from '../api/types';
import { formatChange, formatDate, formatPrice } from '../lib/format';

/** Why a list is empty when the model hasn't published anything. */
export function statusMessage(status: PredictionStatus): string {
  switch (status) {
    case 'Withheld':
      return "The latest model didn't beat predicting \"no change\" on recent weeks it hadn't seen, so its predictions are hidden.";
    case 'Published':
      return 'No predictions in this direction right now.';
    case 'Preview':
      return 'No strong early market signal in this direction right now.';
    default:
      return 'Predictions start once there is enough price history to train the model and test it on weeks it hasn’t seen.';
  }
}

/** The features that moved one prediction most, each with its push up or down. */
export function Reasons({ reasons }: { reasons: PredictionReason[] }) {
  return (
    <ul className="grid gap-1 text-sm">
      {reasons.map((r) => (
        <li key={r.feature} className="flex items-baseline justify-between gap-3">
          <span className="text-slate-700">{r.text}</span>
          <span className={`shrink-0 text-xs font-semibold tabular-nums ${r.effectPercent >= 0 ? 'gain' : 'loss'}`}>
            {formatChange(r.effectPercent)}
          </span>
        </li>
      ))}
    </ul>
  );
}

function ChangeBadge({ change }: { change: number }) {
  return <span className={`font-semibold tabular-nums ${change >= 0 ? 'gain' : 'loss'}`}>{formatChange(change)}</span>;
}

/** A card page's forecast: one row per printing with the predicted price, its likely range and the reasons. */
export function CardOutlook({ list }: { list: PredictionList }) {
  if (list.cards.length === 0) {
    return <p className="text-sm text-slate-600">{list.status === 'Published' ? 'This card is under the model’s $1 minimum or has no recent price.' : statusMessage(list.status)}</p>;
  }
  return (
    <div className="grid gap-4">
      {list.cards.map((p) => (
        <div key={p.variant} className="grid gap-2">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <span className="text-sm font-semibold text-slate-600">{p.variantLabel}</span>
            <span>
              <span className="price text-lg text-slate-900">{formatPrice(p.predicted)}</span>{' '}
              <span className="text-sm">
                <ChangeBadge change={p.changePercent} />{' '}
                <span className="text-slate-500">
                  from {formatPrice(p.current)} on {formatDate(list.asOf, 'short')}
                </span>
              </span>
            </span>
          </div>
          <p className="text-xs text-slate-500">
            Likely range {formatPrice(p.low)} – {formatPrice(p.high)} (8 in 10 past outcomes fell in a range this wide)
          </p>
          {p.reasons.length > 0 && <Reasons reasons={p.reasons} />}
        </div>
      ))}
    </div>
  );
}

/** Predicted risers or fallers, each linking to its card. */
export function PredictionTable({ cards, empty, status }: { cards: CardPrediction[]; empty: string; status: PredictionStatus }) {
  if (cards.length === 0) return <p className="px-4 py-6 text-sm text-slate-500">{empty}</p>;
  return (
    <ol className="divide-y divide-slate-100">
      {cards.map((p) => (
        <li key={`${p.cardId}-${p.variant}`}>
          <Link to={`/cards/${p.cardId}`} className="block px-4 py-2.5 hover:bg-slate-50">
            <span className="flex items-center gap-3">
              {p.imageUrl ? (
                <img src={p.imageUrl} alt="" loading="lazy" className="h-12 w-9 shrink-0 rounded object-cover" />
              ) : (
                <span className="h-12 w-9 shrink-0 rounded bg-slate-200" aria-hidden="true" />
              )}
              <span className="min-w-0 flex-1">
                <span className="block truncate font-semibold text-slate-900">{p.name}</span>
                <span className="block truncate text-xs text-slate-500">
                  {p.setName} · {p.variantLabel}
                </span>
                {p.reasons[0] && <span className="block truncate text-xs text-slate-500">{p.reasons[0].text}</span>}
              </span>
              <span className="shrink-0 whitespace-nowrap text-right">
                <span className="price block text-slate-900">{formatPrice(status === 'Preview' ? p.current : p.predicted)}</span>
                <span className="block text-xs">
                  <ChangeBadge change={p.changePercent} />
                  <span className="text-slate-400 sm:hidden"> </span>
                  <span className="block text-slate-400 sm:inline">
                    {status === 'Preview' ? ' early signal' : ` from ${formatPrice(p.current)}`}
                  </span>
                </span>
              </span>
            </span>
          </Link>
        </li>
      ))}
    </ol>
  );
}
