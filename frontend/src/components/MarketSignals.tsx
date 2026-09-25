import { Link } from 'react-router-dom';

import type { SleeperCard, TrendingCard } from '../api/types';
import { formatChange, formatPrice } from '../lib/format';
import { Sparkline } from './Sparkline';

function Thumb({ imageUrl }: { imageUrl: string | null }) {
  return imageUrl ? (
    <img src={imageUrl} alt="" loading="lazy" className="h-12 w-9 shrink-0 rounded object-cover" />
  ) : (
    <span className="h-12 w-9 shrink-0 rounded bg-slate-200" aria-hidden="true" />
  );
}

function CardName({ name, setName, variantLabel }: { name: string; setName: string; variantLabel: string }) {
  return (
    <span className="min-w-0 flex-1">
      <span className="block truncate font-semibold text-slate-900">{name}</span>
      <span className="block truncate text-xs text-slate-500">
        {setName} · {variantLabel}
      </span>
    </span>
  );
}

/** Cards whose price has fallen steadily, each with its daily prices as a sparkline. */
export function TrendingDownList({ cards, empty }: { cards: TrendingCard[]; empty: string }) {
  if (cards.length === 0) return <p className="px-4 py-6 text-sm text-slate-500">{empty}</p>;
  return (
    <ol className="divide-y divide-slate-100">
      {cards.map((c) => (
        <li key={`${c.cardId}-${c.variant}`}>
          <Link to={`/cards/${c.cardId}`} className="flex items-center gap-3 px-4 py-2.5 hover:bg-slate-50">
            <Thumb imageUrl={c.imageUrl} />
            <CardName name={c.name} setName={c.setName} variantLabel={c.variantLabel} />
            <Sparkline points={c.points} className="loss hidden shrink-0 sm:block" />
            <span className="text-right">
              <span className="price block text-slate-900">{formatPrice(c.to)}</span>
              <span className="loss block text-xs font-semibold tabular-nums">
                {formatChange(c.changePercent)} <span className="font-normal text-slate-400">from {formatPrice(c.from)}</span>
              </span>
            </span>
          </Link>
        </li>
      ))}
    </ol>
  );
}

/** Cards whose cheapest listing sits well above what they've been selling for. */
export function SleepersList({ cards, empty }: { cards: SleeperCard[]; empty: string }) {
  if (cards.length === 0) return <p className="px-4 py-6 text-sm text-slate-500">{empty}</p>;
  return (
    <ol className="divide-y divide-slate-100">
      {cards.map((c) => (
        <li key={`${c.cardId}-${c.variant}`}>
          <Link to={`/cards/${c.cardId}`} className="flex items-center gap-3 px-4 py-2.5 hover:bg-slate-50">
            <Thumb imageUrl={c.imageUrl} />
            <span className="min-w-0 flex-1">
              <span className="block truncate font-semibold text-slate-900">{c.name}</span>
              <span className="block truncate text-xs text-slate-500">
                {c.setName} · {c.variantLabel}
              </span>
              <span className="block truncate text-xs text-slate-500">
                <span className="gain font-semibold">{formatChange(c.listingGapPercent)}</span> listing gap
                {c.change30Percent !== null && ` · 30 days ${formatChange(c.change30Percent)}`}
              </span>
            </span>
            <span className="text-right">
              <span className="block text-xs text-slate-500">
                sells <span className="price text-sm text-slate-900">{formatPrice(c.market)}</span>
              </span>
              <span className="block text-xs text-slate-500">
                listed from <span className="font-semibold text-slate-900">{formatPrice(c.low)}</span>
              </span>
            </span>
          </Link>
        </li>
      ))}
    </ol>
  );
}
