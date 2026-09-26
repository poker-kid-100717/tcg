import { useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { useChecklist, useCollectionActions } from '../api/collection';
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
  const show = (['owned', 'missing'] as const).find((v) => v === params.get('show')) ?? '';
  const [filter, setFilter] = useState('');
  const checklist = useChecklist(setId);
  const { add } = useCollectionActions();
  const [justAdded, setJustAdded] = useState<string | null>(null);
  const owned = useMemo(() => new Map((checklist.data?.owned ?? []).map((o) => [o.cardId, o.quantity])), [checklist.data]);

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
      .filter((c) => !show || (show === 'owned') === owned.has(c.id))
      .filter((c) => !needle || c.name.toLowerCase().includes(needle) || c.number.toLowerCase() === needle)
      .sort(SORTS[sort].compare);
  }, [data, rarity, filter, sort, show, owned]);

  if (isPending) return <Loading label="Loading set…" />;
  if (error) return <ErrorState error={error} onRetry={() => refetch()} />;

  const { set, stats } = data;
  const ownedBase = data.cards.filter((c) => owned.has(c.id) && !isSecret(c.number, set.printedTotal)).length;
  const quickAdd = (card: CardSummary) =>
    add.mutate(
      { cardId: card.id, variant: card.priceVariant ?? 'normal' },
      { onSuccess: () => setJustAdded(`Added ${card.name} to your collection.`) },
    );
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

      {checklist.data && owned.size > 0 && (
        <section aria-label="Your progress" className="panel mb-8 grid gap-3 p-5 md:grid-cols-[1fr_auto_auto] md:items-center">
          <div className="grid gap-2">
            <p className="font-semibold text-slate-900">
              You have {ownedBase} of {set.printedTotal} ({Math.round((ownedBase / Math.max(1, set.printedTotal)) * 100)}%)
            </p>
            <span className="h-2 overflow-hidden rounded-full bg-slate-100">
              <span className="block h-full rounded-full bg-pokemon-blue" style={{ width: `${Math.min(100, (ownedBase / Math.max(1, set.printedTotal)) * 100)}%` }} />
            </span>
          </div>
          <div className="text-sm">
            <p className="text-slate-500">To finish the main set</p>
            <p className="price text-xl text-slate-900">{formatTotal(checklist.data.costToCompleteBase)}</p>
          </div>
          <div className="text-sm">
            <p className="text-slate-500">Including secret rares</p>
            <p className="price text-xl text-slate-900">{formatTotal(checklist.data.costToCompleteAll)}</p>
          </div>
        </section>
      )}

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
        {owned.size > 0 && (
          <label className="grid gap-1 text-sm font-medium">
            Show
            <select className="input w-40" value={show} onChange={(e) => update('show', e.target.value)}>
              <option value="">All cards</option>
              <option value="owned">Cards I have</option>
              <option value="missing">Cards I need</option>
            </select>
          </label>
        )}
        <p className="ml-auto text-sm text-slate-500" aria-live="polite">
          {cards.length} of {data.cards.length} cards
        </p>
      </div>

      <p role="status" className="mb-3 text-sm text-emerald-700">
        {justAdded}
        {add.error && <span className="text-red-700">{add.error.message}</span>}
      </p>

      {cards.length === 0 ? (
        <p className="panel p-10 text-center text-slate-500">No cards match those filters.</p>
      ) : (
        <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5 xl:grid-cols-6">
          {cards.map((card) => (
            <li key={card.id} className="grid gap-1.5">
              <CardTile
                id={card.id}
                name={card.name}
                number={card.number}
                imageUrl={card.imageUrl}
                price={card.marketPrice}
                subtitle={`#${card.number}${card.rarity ? ` · ${card.rarity}` : ''}`}
                badge={
                  owned.has(card.id) ? (
                    <span className="rounded-full bg-emerald-600 px-2 py-0.5 text-[11px] font-bold text-white shadow">
                      Have {owned.get(card.id)}
                    </span>
                  ) : undefined
                }
              />
              <button
                type="button"
                className="rounded-lg border border-slate-200 bg-white py-1 text-xs font-semibold text-pokemon-pokeblue hover:border-pokemon-blue"
                onClick={() => quickAdd(card)}
                aria-label={`Add ${card.name} #${card.number} to collection`}
              >
                + Add
              </button>
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

/** Numbered past the printed total ("SV 205/198"): a secret rare, left out of "the main set". */
const isSecret = (number: string, printedTotal: number) => /^\d+$/.test(number) && Number(number) > printedTotal;
