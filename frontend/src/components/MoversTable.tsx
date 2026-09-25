import { Link } from 'react-router-dom';

import type { PriceMove } from '../api/types';
import { formatChange, formatPrice } from '../lib/format';

/** Biggest price moves: card, printing, then and now, and the change. */
export function MoversTable({ title, moves, tone }: { title: string; moves: PriceMove[]; tone: 'gain' | 'loss' }) {
  return (
    <section aria-label={title} className="panel overflow-hidden">
      <h3 className="border-b border-slate-200 px-4 py-3 text-base">{title}</h3>
      {moves.length === 0 ? (
        <p className="px-4 py-6 text-sm text-slate-500">No moves over $2 in this window yet.</p>
      ) : (
        <ol className="divide-y divide-slate-100">
          {moves.map((m) => (
            <li key={`${m.cardId}-${m.variant}`}>
              <Link to={`/cards/${m.cardId}`} className="flex items-center gap-3 px-4 py-2.5 hover:bg-slate-50">
                {m.imageUrl ? (
                  <img src={m.imageUrl} alt="" loading="lazy" className="h-12 w-9 shrink-0 rounded object-cover" />
                ) : (
                  <span className="h-12 w-9 shrink-0 rounded bg-slate-200" aria-hidden="true" />
                )}
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-semibold text-slate-900">{m.name}</span>
                  <span className="block truncate text-xs text-slate-500">
                    {m.setName} · {m.variantLabel}
                  </span>
                </span>
                <span className="text-right">
                  <span className="price block text-slate-900">{formatPrice(m.to)}</span>
                  <span className={`block text-xs font-semibold tabular-nums ${tone}`}>
                    {formatChange(m.changePercent)} <span className="font-normal text-slate-400">from {formatPrice(m.from)}</span>
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
