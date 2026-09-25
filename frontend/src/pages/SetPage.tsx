import { useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { useSet } from '../api/hooks';
import type { CardSummary } from '../api/types';
import { CardTile } from '../components/CardTile';
import { ErrorState, Loading } from '../components/States';
import { compareCollectorNumbers, formatDate, formatPrice, formatTotal } from '../lib/format';

const SORTS = {
  number: { label: 'Collector number', compare: (a: CardSummary, b: CardSummary) => compareCollectorNumbers(a.number, b.number) },
  'price-desc': {
    label: 'Price: high to low',
    compare: (a: CardSummary, b: CardSummary) => (b.marketPrice ?? -1) - (a.marketPrice ?? -1),
  },
  'price-asc': {
    label: 'Price: low to high',
    compare: (a: CardSummary, b: CardSummary) => (a.marketPrice ?? Infinity) - (b.marketPrice ?? Infinity),
  },
  name: { label: 'Name', compare: (a: CardSummary, b: CardSummary) => a.name.localeCompare(b.name) },
} as const;
type SortKey = keyof typeof SORTS;

export default function SetPage() {
  const { setId = '' } = useParams();
  const { data, isPending, error, refetch } = useSet(setId);
  // Sort, rarity and filter live in the URL, so a filtered view can be shared.
  const [params, setParams] = useSearchParams();
  const sort = (params.get('sort') as SortKey) in SORTS ? (params.get('sort') as SortKey) : 'number';
  const rarity = params.get('rarity') ?? '';
  const [filter, setFilter] = useState('');

  const update = (key: string, value: string) =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: true },
    );

  const rarities = useMemo(
    () => [...new Set((data?.cards ?? []).map((c) => c.rarity).filter((r): r is string => !!r))].sort(),
    [data],
  );
  const cards = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return (data?.cards ?? [])
      .filter((c) => !rarity || c.rarity === rarity)
      .filter((c) => !needle || c.name.toLowerCase().includes(needle) || c.number.toLowerCase() === needle)
      .sort(SORTS[sort].compare);
  }, [data, rarity, filter, sort]);

  if (isPending) return <Loading label="Loading set…" />;
  if (error) return <ErrorState error={error} onRetry={() => refetch()} />;

  const { set, stats } = data;
  return (
    <div className="container-custom py-8 sm:py-10">
      <nav aria-label="Breadcrumb" className="mb-6 text-sm text-slate-500">
        <Link to="/sets" className="hover:text-slate-900">
          Sets
        </Link>{' '}
        / <span className="text-slate-900">{set.name}</span>
      </nav>

      <header className="mb-8 grid items-center gap-6 md:grid-cols-[auto_1fr]">
        {set.logoUrl && <img src={set.logoUrl} alt="" className="max-h-24 w-auto max-w-[260px] object-contain" />}
        <div className="grid gap-1">
          <p className="eyebrow">{set.series}</p>
          <h1 className="text-3xl sm:text-4xl">{set.name}</h1>
          <p className="text-slate-600">
            Released {formatDate(set.releaseDate)} · {set.printedTotal} cards
            {set.total > set.printedTotal ? ` + ${set.total - set.printedTotal} secret` : ''}
          </p>
        </div>
      </header>

      <dl className="mb-8 grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Set market value" value={formatTotal(stats.totalMarketValue)} hint={`Sum of ${stats.pricedCount} priced cards`} />
        <Stat label="Cards" value={String(stats.cardCount)} />
        <Stat label="Priced on TCGplayer" value={String(stats.pricedCount)} />
        <Stat
          label="Most valuable"
          value={formatPrice(stats.mostValuable?.marketPrice)}
          hint={stats.mostValuable?.name}
          href={stats.mostValuable ? `/cards/${stats.mostValuable.id}` : undefined}
        />
      </dl>

      <div className="mb-5 flex flex-wrap items-end gap-3">
        <label className="grid gap-1 text-sm font-medium">
          Find in set
          <input className="input w-56" type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Name or number" />
        </label>
        <label className="grid gap-1 text-sm font-medium">
          Rarity
          <select className="input w-48" value={rarity} onChange={(e) => update('rarity', e.target.value)}>
            <option value="">All rarities</option>
            {rarities.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-sm font-medium">
          Sort by
          <select className="input w-48" value={sort} onChange={(e) => update('sort', e.target.value === 'number' ? '' : e.target.value)}>
            {Object.entries(SORTS).map(([key, { label }]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <p className="ml-auto text-sm text-slate-500" aria-live="polite">
          {cards.length} of {data.cards.length} cards
        </p>
      </div>

      {cards.length === 0 ? (
        <p className="panel p-10 text-center text-slate-500">No cards match those filters.</p>
      ) : (
        <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5 xl:grid-cols-6">
          {cards.map((card) => (
            <li key={card.id}>
              <CardTile
                id={card.id}
                name={card.name}
                number={card.number}
                imageUrl={card.imageUrl}
                price={card.marketPrice}
                subtitle={`#${card.number}${card.rarity ? ` · ${card.rarity}` : ''}`}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Stat({ label, value, hint, href }: { label: string; value: string; hint?: string; href?: string }) {
  return (
    <div className="panel grid gap-0.5 p-4">
      <dt className="text-xs font-medium text-slate-500">{label}</dt>
      <dd className="price text-2xl text-slate-900">{value}</dd>
      {hint && (
        <dd className="line-clamp-1 text-xs text-slate-500">
          {href ? (
            <Link to={href} className="hover:text-pokemon-blue hover:underline">
              {hint}
            </Link>
          ) : (
            hint
          )}
        </dd>
      )}
    </div>
  );
}
